using System.Globalization;
using Arma.Modules.Activities.Domain;
using Arma.Modules.Activities.Infrastructure;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Activities.Application;

/// <summary>Gelişmiş aramaya aktivite ölçütleri ve panoya görev sayıları.</summary>
internal sealed class ActivitiesInsights(ActivitiesDbContext db, ICurrentUser user, TimeProvider clock) : IContactCriteriaProvider, IDashboardContributor
{
    private const string Group = "Aktivite";

    public Task<IReadOnlyList<CriterionDefinition>> DefinitionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CriterionDefinition>>(
        [
            new("activity_none_days", Group, "Son ... gündür aktivite yok", CriterionKind.Days),
            new("activity_open_task", Group, "Açık görevi var", CriterionKind.Flag),
        ]);

    public async Task<CriterionMatch?> MatchAsync(string key, string? value, CancellationToken cancellationToken)
    {
        switch (key)
        {
            case "activity_none_days" when int.TryParse(value, out var days) && days > 0:
                var since = clock.GetUtcNow().AddDays(-days);
                var recent = await db.Participants
                    .Where(p => db.Activities.Any(a => a.Id == p.ActivityId && (a.OccurredAt ?? a.CreatedAt) >= since))
                    .Select(p => p.ContactId).Distinct().ToListAsync(cancellationToken);
                return new CriterionMatch(recent, Exclude: true);
            case "activity_open_task":
                var open = await db.Participants
                    .Where(p => db.Activities.Any(a => a.Id == p.ActivityId && a.Kind == Contracts.ActivityKind.Task && a.Status == ActivityStatus.Open))
                    .Select(p => p.ContactId).Distinct().ToListAsync(cancellationToken);
                return new CriterionMatch(open);
            default:
                return null;
        }
    }

    public async Task<IReadOnlyList<DashboardTile>> TilesAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.Id, out var userId))
        {
            return [];
        }

        var now = clock.GetUtcNow();
        var mine = db.Activities.Where(a => a.AssignedToUserId == userId && a.Kind == Contracts.ActivityKind.Task && a.Status == ActivityStatus.Open);
        var open = await mine.CountAsync(cancellationToken);
        var overdue = await mine.CountAsync(a => a.DueAt < now, cancellationToken);
        return [new("Açık görevlerim", open.ToString("N0", Turkish), overdue > 0 ? $"{overdue} görevin tarihi geçti" : "Geciken görev yok", "tasks", 20, ActivitiesPermissions.Read)];
    }

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
}
