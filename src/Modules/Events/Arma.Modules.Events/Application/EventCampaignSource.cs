using Arma.Modules.Events.Domain;
using Arma.Modules.Events.Infrastructure;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Events.Application;

/// <summary>Kampanyaya etkinlik bağlanır; etkinliğin kendi tarihi esas alınır, kampanya dönemi uygulanmaz.</summary>
internal sealed class EventCampaignSource(EventsDbContext db) : ICampaignSource
{
    public string Kind => "event";

    public string Label => "Etkinlik";

    public int Order => 20;

    public string? Permission => EventsPermissions.Read;

    public async Task<IReadOnlyList<CampaignItem>> ItemsAsync(CancellationToken cancellationToken) =>
        await db.Events.AsNoTracking().OrderByDescending(e => e.StartsAt).Select(e => new CampaignItem(e.Id, e.Title)).ToListAsync(cancellationToken);

    public string Href(Guid id) => $"events/{id}";

    public async Task<IReadOnlyList<CampaignMetric>> MetricsAsync(IReadOnlyCollection<Guid> ids, CampaignPeriod period, CancellationToken cancellationToken)
    {
        var registrations = await Registered(ids).Select(r => new { r.CheckedInAt, r.Payment, r.Amount }).ToListAsync(cancellationToken);
        return
        [
            new("Etkinlik kaydı", registrations.Count, "number"),
            new("Katılan", registrations.Count(r => r.CheckedInAt != null), "number"),
            new("Bilet geliri", registrations.Where(r => r.Payment == PaymentState.Paid).Sum(r => r.Amount), "money", CountsTowardGoal: true),
        ];
    }

    public async Task<IReadOnlyCollection<Guid>> ContactIdsAsync(IReadOnlyCollection<Guid> ids, CampaignPeriod period, CancellationToken cancellationToken) =>
        await Registered(ids).Select(r => r.ContactId).Distinct().ToListAsync(cancellationToken);

    private IQueryable<Registration> Registered(IReadOnlyCollection<Guid> ids) =>
        db.Registrations.AsNoTracking().Where(r => ids.Contains(r.EventId) && r.Status == RegistrationStatus.Registered);
}
