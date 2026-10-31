using Arma.SharedKernel.Modules;

namespace Arma.Modules.Campaigns.Application;

internal sealed class CampaignInsights(CampaignService campaigns) : IDashboardContributor
{
    public async Task<IReadOnlyList<DashboardTile>> TilesAsync(CancellationToken cancellationToken)
    {
        var active = await campaigns.ActiveCountAsync(cancellationToken);
        return [new("Süren kampanya", active.ToString(), active == 0 ? "Süren kampanya yok" : null, "campaigns", 70, CampaignsPermissions.Read)];
    }
}
