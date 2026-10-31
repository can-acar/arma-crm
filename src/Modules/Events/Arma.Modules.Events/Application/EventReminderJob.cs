using Arma.Infrastructure.Messaging;
using Arma.Modules.Comms.Contracts;
using Arma.Modules.Events.Domain;
using Arma.Modules.Events.Infrastructure;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Events.Application;

/// <summary>Etkinlikten bir gün önce kesin kayıtlı katılımcılara hatırlatma e-postası gönderir; her kayda bir kez.</summary>
internal sealed class EventReminderJob(EventsDbContext db, IDbContextOutbox outbox, TimeProvider clock) : IScheduledJob
{
    public string Name => "events.reminders";

    public TimeSpan Interval => TimeSpan.FromMinutes(30);

    public async Task RunAsync(CancellationToken cancellationToken) => await SendAsync(cancellationToken);

    internal async Task<int> SendAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var until = now.AddHours(24);
        var due = await db.Registrations
            .Where(r => r.Status == RegistrationStatus.Registered && !r.ReminderSent)
            .Join(db.Events.Where(e => e.Status == EventStatus.Published && e.StartsAt > now && e.StartsAt <= until), r => r.EventId, e => e.Id, (r, e) => new { r, e })
            .Take(500)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        outbox.Enroll(db);
        foreach (var row in due)
        {
            row.r.ReminderSent = true;
            var place = row.e.Location is null ? "" : $"\nYer: {row.e.Location}";
            await outbox.PublishAsync(new SendTransactionalEmail(row.r.ContactId, $"Hatırlatma: {row.e.Title}",
                $"\"{row.e.Title}\" etkinliği yarın.\n\nTarih: {EventService.When(row.e)}{place}\n\nGirişte bilet kodunuzu gösterin: {row.r.Code}"));
        }

        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return due.Count;
    }
}
