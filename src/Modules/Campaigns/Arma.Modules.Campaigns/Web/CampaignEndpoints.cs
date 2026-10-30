using Arma.Modules.Campaigns.Application;
using Arma.Modules.Campaigns.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Campaigns.Web;

internal static class CampaignEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/campaigns").WithTags("Kampanyalar").RequireAuthorization(CampaignsPermissions.Read);

        group.MapGet("/", (CampaignService service, CampaignStatus? status, CancellationToken ct) => service.ListAsync(status, ct));

        group.MapGet("/{id:guid}", async (CampaignService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } campaign ? Results.Ok(campaign) : Results.NotFound());

        group.MapGet("/{id:guid}/results", async (CampaignService service, Guid id, CancellationToken ct) =>
            await service.ResultsAsync(id, ct) is { } results ? Results.Ok(results) : Results.NotFound());
    }
}
