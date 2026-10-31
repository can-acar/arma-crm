using Arma.Modules.Cases.Domain;
using Arma.Modules.Cases.Infrastructure;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Cases.Application;

/// <summary>Gelişmiş aramaya vaka ölçütleri, panoya açık ve geciken vakalar.</summary>
internal sealed class CasesInsights(CasesDbContext db, CaseService cases, ICurrentUser currentUser) : IContactCriteriaProvider, IDashboardContributor
{
    private const string Group = "Vakalar";

    public async Task<IReadOnlyList<CriterionDefinition>> DefinitionsAsync(CancellationToken cancellationToken)
    {
        var types = await db.Types.AsNoTracking().OrderBy(t => t.Name).Select(t => new CriterionOption(t.Id.ToString(), t.Name)).ToListAsync(cancellationToken);
        return
        [
            new("case_open", Group, "Açık vakası var", CriterionKind.Flag),
            new("case_type", Group, "Bu türde vakası oldu", CriterionKind.Choice, types),
        ];
    }

    public async Task<CriterionMatch?> MatchAsync(string key, string? value, CancellationToken cancellationToken) => key switch
    {
        "case_open" => new(await db.Cases.Where(c => c.Status != CaseStatus.Closed).Select(c => c.ContactId).Distinct().ToListAsync(cancellationToken)),
        "case_type" when Guid.TryParse(value, out var typeId) =>
            new(await db.Cases.Where(c => c.TypeId == typeId).Select(c => c.ContactId).Distinct().ToListAsync(cancellationToken)),
        _ => null,
    };

    public async Task<IReadOnlyList<DashboardTile>> TilesAsync(CancellationToken cancellationToken)
    {
        var userId = Guid.TryParse(currentUser.Id, out var id) ? id : (Guid?)null;
        var summary = await cases.SummaryAsync(userId, cancellationToken);
        return
        [
            new("Açık vaka", summary.Open.ToString("N0"), summary.Overdue > 0 ? $"{summary.Overdue:N0} vakada geciken adım var" : "Geciken adım yok", "cases", 70, CasesPermissions.Read),
            new("Bana atanan vaka", summary.Mine.ToString("N0"), summary.MineOverdue > 0 ? $"{summary.MineOverdue:N0} vakada geciken adım" : null, "cases?mine=1", 71, CasesPermissions.Read),
        ];
    }
}
