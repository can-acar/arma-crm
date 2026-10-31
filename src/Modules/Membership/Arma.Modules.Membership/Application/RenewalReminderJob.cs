using Arma.Modules.Comms.Contracts;
using Arma.Modules.Membership.Domain;
using Arma.Modules.Membership.Infrastructure;
using Arma.Modules.Membership.Web;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Membership.Application;

/// <summary>
/// Dönemi yaklaşan üyelere bilgilendirme e-postası gönderir ("Membership:ReminderDays", varsayılan 30 gün önce;
/// 0 ise kapalı). Her dönem için bir kez gönderilir.
/// </summary>
internal sealed class RenewalReminderJob(MembershipDbContext db, IDbContextOutbox outbox, IConfiguration configuration, TimeProvider clock) : IScheduledJob
{
    public string Name => "membership.renewal-reminders";

    public TimeSpan Interval => TimeSpan.FromHours(6);

    public async Task RunAsync(CancellationToken cancellationToken) => await SendAsync(cancellationToken);

    internal async Task<int> SendAsync(CancellationToken cancellationToken)
    {
        var days = configuration.GetValue("Membership:ReminderDays", 30);
        if (days <= 0)
        {
            return 0;
        }

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var until = today.AddDays(days);
        var due = await db.Members
            .Where(m => m.CancelledOn == null && m.EndOn != null && m.EndOn >= today && m.EndOn <= until && m.StartOn <= today
                && (m.ReminderSentFor == null || m.ReminderSentFor != m.EndOn))
            .Join(db.Types, m => m.TypeId, t => t.Id, (m, t) => new { Member = m, t.Name, t.Fee })
            .Take(500)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        outbox.Enroll(db);
        foreach (var row in due)
        {
            row.Member.ReminderSentFor = row.Member.EndOn;
            var fee = row.Fee > 0 ? $"\n\nYeni dönem aidatı: {MembershipLabels.Money(row.Fee)}." : "";
            await outbox.PublishAsync(new SendTransactionalEmail(row.Member.ContactId, "Üyeliğinizin yenileme zamanı yaklaşıyor",
                $"{row.Name} üyeliğiniz (üye no {row.Member.Number}) {MembershipLabels.Date(row.Member.EndOn!.Value)} tarihinde sona eriyor.{fee}\n\nÜyeliğinizi yenilemek için bizimle iletişime geçebilirsiniz."));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return due.Count;
    }
}
