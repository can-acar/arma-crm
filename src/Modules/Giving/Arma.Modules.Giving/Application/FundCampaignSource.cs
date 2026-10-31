using Arma.Modules.Giving.Domain;
using Arma.Modules.Giving.Infrastructure;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Giving.Application;

/// <summary>Kampanyaya fon bağlanır; dönem içinde o fona gelen tamamlanmış TL bağışları kampanya hedefine sayılır.</summary>
internal sealed class FundCampaignSource(GivingDbContext db) : ICampaignSource
{
    public string Kind => "fund";

    public string Label => "Fon (bağışlar)";

    public int Order => 10;

    public string? Permission => GivingPermissions.Read;

    public async Task<IReadOnlyList<CampaignItem>> ItemsAsync(CancellationToken cancellationToken) =>
        await db.Funds.AsNoTracking().OrderBy(f => f.Name).Select(f => new CampaignItem(f.Id, f.Name)).ToListAsync(cancellationToken);

    public string Href(Guid id) => $"donations?fund={id}";

    public async Task<IReadOnlyList<CampaignMetric>> MetricsAsync(IReadOnlyCollection<Guid> ids, CampaignPeriod period, CancellationToken cancellationToken)
    {
        var donations = await Donations(ids, period).Select(d => new { d.Amount, d.ContactId }).ToListAsync(cancellationToken);
        return
        [
            new("Toplanan bağış", donations.Sum(d => d.Amount), "money", CountsTowardGoal: true),
            new("Bağış sayısı", donations.Count, "number"),
            new("Bağışçı", donations.Select(d => d.ContactId).Distinct().Count(), "number"),
        ];
    }

    public async Task<IReadOnlyCollection<Guid>> ContactIdsAsync(IReadOnlyCollection<Guid> ids, CampaignPeriod period, CancellationToken cancellationToken) =>
        await Donations(ids, period).Select(d => d.ContactId).Distinct().ToListAsync(cancellationToken);

    private IQueryable<Donation> Donations(IReadOnlyCollection<Guid> ids, CampaignPeriod period)
    {
        var query = db.Donations.AsNoTracking()
            .Where(d => d.FundId != null && ids.Contains(d.FundId.Value) && d.Status == DonationStatus.Completed && d.Currency == Donation.DefaultCurrency);
        if (period.From is { } from)
        {
            query = query.Where(d => d.ReceivedOn >= from);
        }

        if (period.To is { } to)
        {
            query = query.Where(d => d.ReceivedOn <= to);
        }

        return query;
    }
}
