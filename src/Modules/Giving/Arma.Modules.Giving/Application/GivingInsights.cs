using System.Globalization;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Giving.Infrastructure;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Giving.Application;

/// <summary>Gelişmiş aramaya bağış ölçütleri, panoya bağış toplamları ve aylık grafik. Toplamlar yalnızca TL bağışları içerir.</summary>
internal sealed class GivingInsights(GivingDbContext db, PledgeService pledges, TimeProvider clock) : IContactCriteriaProvider, IDashboardContributor
{
    private const string Group = "Bağış";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public async Task<IReadOnlyList<CriterionDefinition>> DefinitionsAsync(CancellationToken cancellationToken)
    {
        var funds = await db.Funds.AsNoTracking().OrderBy(f => f.Name).Select(f => new CriterionOption(f.Id.ToString(), f.Name)).ToListAsync(cancellationToken);
        return
        [
            new("gift_since", Group, "Bu tarihten sonra bağış yaptı", CriterionKind.Date),
            new("gift_lapsed_since", Group, "Daha önce bağış yapıp bu tarihten beri yapmadı", CriterionKind.Date),
            new("gift_total_gte", Group, "Toplam bağışı en az (TL)", CriterionKind.Number),
            new("gift_fund", Group, "Bu fona bağış yaptı", CriterionKind.Choice, funds),
            new("gift_never", Group, "Hiç bağış yapmadı", CriterionKind.Flag),
            new("pledge_open", Group, "Ödenmemiş taahhüdü var", CriterionKind.Flag),
            new("pledge_overdue", Group, "Taahhüt taksiti gecikmiş", CriterionKind.Flag),
        ];
    }

    public async Task<CriterionMatch?> MatchAsync(string key, string? value, CancellationToken cancellationToken)
    {
        var completed = db.Donations.Where(d => d.Status == DonationStatus.Completed);
        switch (key)
        {
            case "gift_since" when DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var since):
                return new(await Donors(completed.Where(d => d.ReceivedOn >= since), cancellationToken));
            case "gift_lapsed_since" when DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var since):
                var before = await Donors(completed.Where(d => d.ReceivedOn < since), cancellationToken);
                var after = (await Donors(completed.Where(d => d.ReceivedOn >= since), cancellationToken)).ToHashSet();
                return new(before.Where(id => !after.Contains(id)).ToList());
            case "gift_total_gte" when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var minimum):
                return new(await completed.Where(d => d.Currency == "TRY").GroupBy(d => d.ContactId)
                    .Where(g => g.Sum(d => d.Amount) >= minimum).Select(g => g.Key).ToListAsync(cancellationToken));
            case "gift_fund" when Guid.TryParse(value, out var fund):
                return new(await Donors(completed.Where(d => d.FundId == fund), cancellationToken));
            case "pledge_open":
                var open = await pledges.ListAsync(PledgeState.Open, cancellationToken: cancellationToken);
                var late = await pledges.ListAsync(PledgeState.Overdue, cancellationToken: cancellationToken);
                return new(open.Concat(late).Select(p => p.ContactId).Distinct().ToList());
            case "pledge_overdue":
                return new((await pledges.ListAsync(PledgeState.Overdue, cancellationToken: cancellationToken)).Select(p => p.ContactId).Distinct().ToList());
            case "gift_never":
                return new(await Donors(completed, cancellationToken), Exclude: true);
            default:
                return null;
        }
    }

    public async Task<IReadOnlyList<DashboardTile>> TilesAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var yearStart = new DateOnly(today.Year, 1, 1);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var thisYear = db.Donations.Where(d => d.Status == DonationStatus.Completed && d.Currency == "TRY" && d.ReceivedOn >= yearStart);
        var yearTotal = await thisYear.SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0;
        var monthTotal = await thisYear.Where(d => d.ReceivedOn >= monthStart).SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0;
        var donors = await thisYear.Select(d => d.ContactId).Distinct().CountAsync(cancellationToken);
        var pledgeTotals = await pledges.TotalsAsync(cancellationToken);
        return
        [
            new("Geciken taahhüt", pledgeTotals.Overdue.ToString("N0", Turkish),
                pledgeTotals.Overdue == 0 ? $"Açık {pledgeTotals.Open.ToString("N0", Turkish)} taahhüt" : $"Vadesi geçen {Money.Format(pledgeTotals.OverdueTry, "TRY")}",
                "donations/pledges?state=overdue", 32, GivingPermissions.Read),
            new($"{today.Year} bağışları", Money.Format(yearTotal, "TRY"), $"Bu ay {Money.Format(monthTotal, "TRY")}", "donations", 30, GivingPermissions.Read),
            new($"{today.Year} bağışçıları", donors.ToString("N0", Turkish), "Bu yıl en az bir bağış yapan", "contacts/search", 31, GivingPermissions.Read),
        ];
    }

    public async Task<IReadOnlyList<DashboardChart>> ChartsAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
        var rows = await db.Donations.Where(d => d.Status == DonationStatus.Completed && d.Currency == "TRY" && d.ReceivedOn >= first)
            .GroupBy(d => new { d.ReceivedOn.Year, d.ReceivedOn.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(d => d.Amount) })
            .ToListAsync(cancellationToken);
        var points = Enumerable.Range(0, 12).Select(i => first.AddMonths(i))
            .Select(m => new ChartPoint(m.ToString("MMM yy", Turkish), rows.FirstOrDefault(r => r.Year == m.Year && r.Month == m.Month)?.Total ?? 0))
            .ToList();
        return [new("Aylık bağış (TL, son 12 ay)", points, "money", 30, GivingPermissions.Read)];
    }

    private static async Task<List<Guid>> Donors(IQueryable<Donation> donations, CancellationToken cancellationToken) =>
        await donations.Select(d => d.ContactId).Distinct().ToListAsync(cancellationToken);
}
