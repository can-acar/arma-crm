using System.Globalization;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Events.Domain;
using Arma.Modules.Events.Infrastructure;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Events.Application;

/// <summary>Gelişmiş aramaya etkinlik ölçütleri, panoya yaklaşan etkinlik.</summary>
internal sealed class EventsInsights(EventsDbContext db, TimeProvider clock) : IContactCriteriaProvider, IDashboardContributor
{
    private const string Group = "Etkinlik";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public async Task<IReadOnlyList<CriterionDefinition>> DefinitionsAsync(CancellationToken cancellationToken)
    {
        var events = await db.Events.AsNoTracking().OrderByDescending(e => e.StartsAt).Take(100)
            .Select(e => new CriterionOption(e.Id.ToString(), e.Title)).ToListAsync(cancellationToken);
        return
        [
            new("event_registered", Group, "Bu etkinliğe kayıtlı", CriterionKind.Choice, events),
            new("event_attended", Group, "Bu etkinliğe katıldı", CriterionKind.Choice, events),
            new("event_attended_since", Group, "Bu tarihten sonra bir etkinliğe katıldı", CriterionKind.Date),
        ];
    }

    public async Task<CriterionMatch?> MatchAsync(string key, string? value, CancellationToken cancellationToken)
    {
        var active = db.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled);
        switch (key)
        {
            case "event_registered" when Guid.TryParse(value, out var id):
                return new(await active.Where(r => r.EventId == id).Select(r => r.ContactId).Distinct().ToListAsync(cancellationToken));
            case "event_attended" when Guid.TryParse(value, out var id):
                return new(await active.Where(r => r.EventId == id && r.CheckedInAt != null).Select(r => r.ContactId).Distinct().ToListAsync(cancellationToken));
            case "event_attended_since" when DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var since):
                var from = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), clock.GetLocalNow().Offset);
                return new(await active.Where(r => r.CheckedInAt >= from).Select(r => r.ContactId).Distinct().ToListAsync(cancellationToken));
            default:
                return null;
        }
    }

    public async Task<IReadOnlyList<DashboardTile>> TilesAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var next = await db.Events.AsNoTracking().Where(e => e.EndsAt >= now && e.Status == EventStatus.Published).OrderBy(e => e.StartsAt)
            .Select(e => new { e.Id, e.Title, e.StartsAt, e.Capacity, Registered = db.Registrations.Count(r => r.EventId == e.Id && r.Status == RegistrationStatus.Registered) })
            .FirstOrDefaultAsync(cancellationToken);
        if (next is null)
        {
            return [new("Yaklaşan etkinlik", "Yok", "Yayımlanmış etkinlik yok", "events", 50, EventsPermissions.Read)];
        }

        var seats = next.Capacity is { } c ? $"{next.Registered}/{c} kayıt" : $"{next.Registered} kayıt";
        return [new(next.Title, next.StartsAt.ToLocalTime().ToString("d MMMM HH:mm", Turkish), seats, $"events/{next.Id}", 50, EventsPermissions.Read)];
    }
}
