using Arma.Modules.Cases.Application;
using Arma.Modules.Cases.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Cases.Web;

/// <summary>Salt okunur vaka API'si. Notlar API'de verilmez.</summary>
internal static class CaseEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/cases").WithTags("Vakalar").RequireAuthorization(CasesPermissions.Read);

        group.MapGet("/", (CaseService service, CaseStatus? status, Guid? contactId, Guid? typeId, int? page, CancellationToken ct) =>
            service.ListAsync(new CaseFilter(Status: status, ContactId: contactId, TypeId: typeId, Page: page ?? 1, PageSize: 100), ct));

        group.MapGet("/{id:guid}", async (CaseService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } item ? Results.Ok(item) : Results.NotFound());

        group.MapGet("/types", (CaseService service, CancellationToken ct) => service.TypesAsync(cancellationToken: ct));
    }
}
