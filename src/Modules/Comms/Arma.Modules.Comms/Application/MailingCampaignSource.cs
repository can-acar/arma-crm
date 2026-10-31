using Arma.Modules.Comms.Domain;
using Arma.Modules.Comms.Infrastructure;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Comms.Application;

/// <summary>Kampanyaya toplu ileti bağlanır; gönderilen ileti ve ulaşılan kişi sayılır.</summary>
internal sealed class MailingCampaignSource(CommsDbContext db) : ICampaignSource
{
    public string Kind => "mailing";

    public string Label => "Toplu ileti";

    public int Order => 30;

    public string? Permission => CommsPermissions.Mailings;

    public async Task<IReadOnlyList<CampaignItem>> ItemsAsync(CancellationToken cancellationToken) =>
        await db.Mailings.AsNoTracking().OrderByDescending(m => m.CreatedAt).Select(m => new CampaignItem(m.Id, m.Name)).ToListAsync(cancellationToken);

    public string Href(Guid id) => $"comms/mailings/{id}";

    public async Task<IReadOnlyList<CampaignMetric>> MetricsAsync(IReadOnlyCollection<Guid> ids, CampaignPeriod period, CancellationToken cancellationToken)
    {
        var messages = await db.Messages.AsNoTracking().Where(m => m.MailingId != null && ids.Contains(m.MailingId.Value))
            .GroupBy(m => m.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        return
        [
            new("Gönderilen ileti", messages.Where(m => m.Status == MessageStatus.Sent).Sum(m => m.Count), "number"),
            new("Gönderilemeyen", messages.Where(m => m.Status is MessageStatus.Failed or MessageStatus.Skipped).Sum(m => m.Count), "number"),
        ];
    }

    public async Task<IReadOnlyCollection<Guid>> ContactIdsAsync(IReadOnlyCollection<Guid> ids, CampaignPeriod period, CancellationToken cancellationToken) =>
        await db.Messages.AsNoTracking()
            .Where(m => m.MailingId != null && ids.Contains(m.MailingId.Value) && m.Status == MessageStatus.Sent && m.ContactId != null)
            .Select(m => m.ContactId!.Value).Distinct().ToListAsync(cancellationToken);
}
