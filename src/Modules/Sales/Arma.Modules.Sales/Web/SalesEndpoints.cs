using Arma.Modules.Sales.Application;
using Arma.Modules.Sales.Domain;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Sales.Web;

internal static class SalesEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/sales").WithTags("Satış").RequireAuthorization(SalesPermissions.Read);

        group.MapGet("/pipelines", (SalesService service, CancellationToken ct) => service.PipelinesAsync(ct));

        group.MapGet("/board", (SalesService service, Guid? pipelineId, Guid? ownerUserId, string? search, CancellationToken ct) =>
            service.BoardAsync(pipelineId, new BoardFilter(ownerUserId, search), ct));

        group.MapGet("/opportunities/{id:guid}", async (SalesService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } item ? Results.Ok(item) : Results.NotFound());

        group.MapPost("/opportunities", async (SalesService service, CreateOpportunityRequest request, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(request.Opportunity, request.PipelineId, request.StageId, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/sales/opportunities/{result.Value}", new { id = result.Value }) : Problem(result.Error!);
            })
            .RequireAuthorization(SalesPermissions.Write);

        group.MapPut("/opportunities/{id:guid}", async (SalesService service, Guid id, OpportunityInput request, CancellationToken ct) =>
                NoContent(await service.UpdateAsync(id, request, ct)))
            .RequireAuthorization(SalesPermissions.Write);

        group.MapPost("/opportunities/{id:guid}/move", async (SalesService service, Guid id, MoveRequest request, CancellationToken ct) =>
                NoContent(await service.MoveAsync(id, request.StageId, request.BeforeId, request.LostReason, ct)))
            .RequireAuthorization(SalesPermissions.Write);

        group.MapGet("/quotes", (QuoteService service, QuoteStatus? status, Guid? contactId, Guid? opportunityId, CancellationToken ct) =>
            service.ListAsync(status, contactId, opportunityId, ct));

        group.MapGet("/quotes/{id:guid}", async (QuoteService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } quote ? Results.Ok(quote) : Results.NotFound());

        group.MapPost("/quotes", async (QuoteService service, QuoteInput request, CancellationToken ct) =>
            {
                var result = await service.SaveAsync(null, request, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/sales/quotes/{result.Value}", new { id = result.Value }) : Problem(result.Error!);
            })
            .RequireAuthorization(SalesPermissions.Write);

        group.MapPost("/quotes/{id:guid}/status", async (QuoteService service, Guid id, StatusRequest request, CancellationToken ct) =>
                NoContent(await service.SetStatusAsync(id, request.Status, ct)))
            .RequireAuthorization(SalesPermissions.Write);
    }

    internal sealed record CreateOpportunityRequest(OpportunityInput Opportunity, Guid? PipelineId, Guid? StageId);

    internal sealed record MoveRequest(Guid StageId, Guid? BeforeId, string? LostReason);

    internal sealed record StatusRequest(QuoteStatus Status);

    private static IResult NoContent(Result<Guid> result) => result.IsSuccess
        ? Results.NoContent()
        : result.Error == SalesErrors.NotFound || result.Error == QuoteErrors.NotFound ? Results.NotFound() : Problem(result.Error!);

    private static IResult Problem(Error error) =>
        Results.Problem(title: error.Message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
