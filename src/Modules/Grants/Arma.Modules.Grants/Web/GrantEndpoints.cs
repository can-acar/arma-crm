using Arma.Modules.Grants.Application;
using Arma.Modules.Grants.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Grants.Web;

/// <summary>Salt okunur hibe API'si.</summary>
internal static class GrantEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/grants").WithTags("Hibeler").RequireAuthorization(GrantsPermissions.Read);

        group.MapGet("/", (GrantService service, GrantStatus? status, Guid? funderId, int? year, CancellationToken ct) =>
            service.ListAsync(new GrantFilter(Status: status, FunderId: funderId, Year: year), ct));

        group.MapGet("/{id:guid}", async (GrantService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } item ? Results.Ok(item) : Results.NotFound());
    }
}
