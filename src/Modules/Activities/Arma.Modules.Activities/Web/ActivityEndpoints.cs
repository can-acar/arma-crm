using Arma.Modules.Activities.Application;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Activities.Domain;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Activities.Web;

internal static class ActivityEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/activities").WithTags("Aktiviteler").RequireAuthorization(ActivitiesPermissions.Read);

        group.MapGet("/", (ActivityService service, Guid? contactId, Guid? assignedTo, ActivityKind? kind, ActivityStatus? status, bool overdue = false, string? search = null, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            service.ListAsync(new ActivityFilter(contactId, assignedTo, kind, status, overdue, search, page, pageSize), ct));

        group.MapGet("/{id:guid}", async (ActivityService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } activity ? Results.Ok(activity) : Results.NotFound());

        group.MapPost("/", async (ActivityService service, SaveActivityRequest request, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(request, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/activities/{result.Value}", new { id = result.Value }) : Problem(result.Error!);
            })
            .RequireAuthorization(ActivitiesPermissions.Write);

        group.MapPut("/{id:guid}", async (ActivityService service, Guid id, SaveActivityRequest request, CancellationToken ct) =>
                NoContent(await service.UpdateAsync(id, request, ct)))
            .RequireAuthorization(ActivitiesPermissions.Write);

        group.MapPost("/{id:guid}/complete", async (ActivityService service, Guid id, CancellationToken ct) =>
                NoContent(await service.CompleteAsync(id, ct)))
            .RequireAuthorization(ActivitiesPermissions.Write);

        group.MapPost("/{id:guid}/reopen", async (ActivityService service, Guid id, CancellationToken ct) =>
                NoContent(await service.ReopenAsync(id, ct)))
            .RequireAuthorization(ActivitiesPermissions.Write);

        group.MapPost("/{id:guid}/cancel", async (ActivityService service, Guid id, CancellationToken ct) =>
                NoContent(await service.CancelAsync(id, ct)))
            .RequireAuthorization(ActivitiesPermissions.Write);

        group.MapDelete("/{id:guid}", async (ActivityService service, Guid id, CancellationToken ct) =>
                await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(ActivitiesPermissions.Delete);
    }

    private static IResult NoContent(Result<Guid> result) => result.IsSuccess
        ? Results.NoContent()
        : result.Error == ActivityErrors.NotFound ? Results.NotFound() : Problem(result.Error!);

    private static IResult Problem(Error error) =>
        Results.Problem(title: error.Message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
