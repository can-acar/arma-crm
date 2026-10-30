using Arma.Modules.Contacts.Application;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Contacts.Web;

internal static class ContactEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/contacts").WithTags("Kişiler").RequireAuthorization(ContactsPermissions.Read);

        group.MapGet("/", (ContactService service, string? search, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
            service.ListAsync(search, page, pageSize, ct));

        group.MapGet("/{id:guid}", async (ContactService service, Guid id, CancellationToken ct) =>
            await service.FindAsync(id, ct) is { } contact ? Results.Ok(contact) : Results.NotFound());

        group.MapPost("/people", async (ContactService service, CreatePersonRequest request, CancellationToken ct) =>
            ToResponse(await service.CreatePersonAsync(request, ct)))
            .RequireAuthorization(ContactsPermissions.Write);

        group.MapPost("/organizations", async (ContactService service, CreateOrganizationRequest request, CancellationToken ct) =>
            ToResponse(await service.CreateOrganizationAsync(request, ct)))
            .RequireAuthorization(ContactsPermissions.Write);
    }

    private static IResult ToResponse(Result<Guid> result) => result.IsSuccess
        ? Results.Created($"/api/v1/contacts/{result.Value}", new { id = result.Value })
        : Results.Problem(title: result.Error!.Message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
}
