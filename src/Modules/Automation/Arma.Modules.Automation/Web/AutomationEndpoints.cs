using Arma.Modules.Automation.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Automation.Web;

internal static class AutomationEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/automation").WithTags("Otomasyon").RequireAuthorization(AutomationPermissions.Read);

        group.MapGet("/rules", (AutomationService service, CancellationToken ct) => service.ListAsync(ct));

        group.MapGet("/rules/{id:guid}", async (AutomationService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } rule ? Results.Ok(rule) : Results.NotFound());

        group.MapGet("/rules/{id:guid}/runs", (AutomationService service, Guid id, CancellationToken ct) => service.RunsAsync(id, 100, ct));
    }
}
