using System.Globalization;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Sales.Domain;
using Arma.Modules.Sales.Infrastructure;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Sales.Application;

/// <summary>Gelişmiş aramaya satış ölçütleri, panoya açık satış hattı ve bu ay kazanılan.</summary>
internal sealed class SalesInsights(SalesDbContext db, SalesService sales) : IContactCriteriaProvider, IDashboardContributor
{
    private const string Group = "Satış";

    public Task<IReadOnlyList<CriterionDefinition>> DefinitionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CriterionDefinition>>(
        [
            new("sales_open", Group, "Açık satış fırsatı var", CriterionKind.Flag),
            new("sales_won_since", Group, "Bu tarihten sonra satış kazanıldı", CriterionKind.Date),
            new("sales_never", Group, "Hiç satış fırsatı olmadı", CriterionKind.Flag),
        ]);

    public async Task<CriterionMatch?> MatchAsync(string key, string? value, CancellationToken cancellationToken)
    {
        var withStage = db.Opportunities.Join(db.Stages, o => o.StageId, s => s.Id, (o, s) => new { o.ContactId, s.Kind, o.ClosedOn });
        switch (key)
        {
            case "sales_open":
                return new(await withStage.Where(x => x.Kind == StageKind.Open).Select(x => x.ContactId).Distinct().ToListAsync(cancellationToken));
            case "sales_won_since" when DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var since):
                return new(await withStage.Where(x => x.Kind == StageKind.Won && x.ClosedOn >= since).Select(x => x.ContactId).Distinct().ToListAsync(cancellationToken));
            case "sales_never":
                return new(await db.Opportunities.Select(o => o.ContactId).Distinct().ToListAsync(cancellationToken), Exclude: true);
            default:
                return null;
        }
    }

    public async Task<IReadOnlyList<DashboardTile>> TilesAsync(CancellationToken cancellationToken)
    {
        var (open, weighted, won, wonCount) = await sales.SummaryAsync(cancellationToken);
        return
        [
            new("Açık satış hattı", SalesService.Money(open), $"Ağırlıklı tahmin {SalesService.Money(weighted)}", "sales", 60, SalesPermissions.Read),
            new("Bu ay kazanılan", SalesService.Money(won), $"{wonCount} fırsat", "sales", 61, SalesPermissions.Read),
        ];
    }
}
