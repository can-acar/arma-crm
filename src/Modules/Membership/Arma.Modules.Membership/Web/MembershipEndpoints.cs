using Arma.Modules.Membership.Application;
using Arma.Modules.Membership.Domain;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Membership.Web;

internal static class MembershipEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/memberships").WithTags("Üyelik").RequireAuthorization(MembershipPermissions.Read);

        group.MapGet("/", (MembershipService service, MembershipStatus? status, Guid? typeId, Guid? contactId, DateOnly? endsBefore, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            service.ListAsync(new MemberFilter(status, typeId, contactId, endsBefore, page, Math.Clamp(pageSize, 1, 500)), ct));

        group.MapGet("/{id:guid}", async (MembershipService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } member ? Results.Ok(member) : Results.NotFound());

        group.MapGet("/{id:guid}/payments", (MembershipService service, Guid id, CancellationToken ct) => service.PaymentsAsync(id, ct));

        group.MapPost("/", async (MembershipService service, JoinRequest request, CancellationToken ct) =>
            {
                var result = await service.JoinAsync(request, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/memberships/{result.Value}", new { id = result.Value }) : Problem(result.Error!);
            })
            .RequireAuthorization(MembershipPermissions.Write);

        group.MapPost("/{id:guid}/renew", async (MembershipService service, Guid id, DuesInput? dues, CancellationToken ct) =>
                NoContent(await service.RenewAsync(id, dues, ct)))
            .RequireAuthorization(MembershipPermissions.Write);

        group.MapPost("/{id:guid}/payments", async (MembershipService service, Guid id, DuesInput dues, CancellationToken ct) =>
                NoContent(await service.RecordDuesAsync(id, dues, ct)))
            .RequireAuthorization(MembershipPermissions.Write);

        group.MapPost("/{id:guid}/cancel", async (MembershipService service, Guid id, CancelRequest request, CancellationToken ct) =>
                NoContent(await service.CancelAsync(id, request.On ?? service.Today, request.Reason, ct)))
            .RequireAuthorization(MembershipPermissions.Write);

        endpoints.MapGet("/api/v1/membership-types", (MembershipService service, bool activeOnly = false, CancellationToken ct = default) => service.TypesAsync(activeOnly, ct))
            .WithTags("Üyelik").RequireAuthorization(MembershipPermissions.Read);
    }

    internal sealed record CancelRequest(DateOnly? On, string? Reason);

    private static IResult NoContent(Result<Guid> result) => result.IsSuccess
        ? Results.NoContent()
        : result.Error == MembershipErrors.NotFound ? Results.NotFound() : Problem(result.Error!);

    private static IResult Problem(Error error) =>
        Results.Problem(title: error.Message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
