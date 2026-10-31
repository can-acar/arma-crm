using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Grants.Domain;
using Arma.Modules.Grants.Infrastructure;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Grants.Application;

/// <summary>Gelişmiş aramaya hibe veren ölçütleri, panoya başvuru ve takvim özetleri.</summary>
internal sealed class GrantsInsights(GrantsDbContext db, GrantService grants) : IContactCriteriaProvider, IDashboardContributor
{
    private const string Group = "Hibeler";

    public Task<IReadOnlyList<CriterionDefinition>> DefinitionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CriterionDefinition>>(
        [
            new("grant_funder", Group, "Hibe başvurusu yapılan kurum", CriterionKind.Flag),
            new("grant_awarded", Group, "Hibe aldığımız kurum", CriterionKind.Flag),
        ]);

    public async Task<CriterionMatch?> MatchAsync(string key, string? value, CancellationToken cancellationToken) => key switch
    {
        "grant_funder" => new(await db.Grants.Select(g => g.FunderId).Distinct().ToListAsync(cancellationToken)),
        "grant_awarded" => new(await db.Grants.Where(g => g.Awarded != null).Select(g => g.FunderId).Distinct().ToListAsync(cancellationToken)),
        _ => null,
    };

    public async Task<IReadOnlyList<DashboardTile>> TilesAsync(CancellationToken cancellationToken)
    {
        var summary = await grants.SummaryAsync(cancellationToken);
        var detail = summary.Overdue > 0 ? $"{summary.Overdue:N0} teslim gecikti"
            : summary.DueSoon > 0 ? $"{GrantService.DueSoonDays} gün içinde {summary.DueSoon:N0} son gün"
            : "Yaklaşan son gün yok";
        return
        [
            new("Bekleyen hibe başvurusu", summary.Pending.ToString("N0"), detail, "grants", 75, GrantsPermissions.Read),
            new($"{grants.Today.Year} hibe kabulü", GrantService.Money(summary.AwardedThisYearTry, "TRY"),
                summary.ExpectedTry > 0 ? $"Beklenen ödeme {GrantService.Money(summary.ExpectedTry, "TRY")}" : null, "grants?status=Awarded", 76, GrantsPermissions.Read),
        ];
    }
}
