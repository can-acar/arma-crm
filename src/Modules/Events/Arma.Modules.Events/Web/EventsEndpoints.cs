using Arma.Modules.Events.Application;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Events.Web;

internal static class EventsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/events").WithTags("Etkinlikler").RequireAuthorization(EventsPermissions.Read);

        group.MapGet("/", (EventService service, bool past = false, CancellationToken ct = default) => service.ListAsync(past, ct));

        group.MapGet("/{id:guid}", async (EventService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } item ? Results.Ok(item) : Results.NotFound());

        group.MapGet("/{id:guid}/participants", (EventService service, Guid id, CancellationToken ct) => service.ParticipantsAsync(id, ct));

        group.MapGet("/{id:guid}/participants.csv", async (EventService service, Guid id, CancellationToken ct) =>
            Results.File(await service.ParticipantsCsvAsync(id, ct), "text/csv; charset=utf-8", "katilimcilar.csv"));

        group.MapPost("/{id:guid}/registrations", async (EventService service, Guid id, RegisterRequest request, CancellationToken ct) =>
            {
                var result = await service.RegisterAsync(id, request.ContactId, request.TicketTypeId, "staff", request.Note, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/events/{id}/participants", new { id = result.Value }) : Problem(result.Error!);
            })
            .RequireAuthorization(EventsPermissions.Write);

        group.MapPost("/registrations/{id:guid}/cancel", async (EventService service, Guid id, CancellationToken ct) =>
                NoContent(await service.CancelRegistrationAsync(id, ct)))
            .RequireAuthorization(EventsPermissions.Write);

        group.MapPost("/{id:guid}/checkin", async (EventService service, Guid id, CheckInRequest request, CancellationToken ct) =>
            {
                var result = await service.CheckInAsync(id, request.Code, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);
            })
            .RequireAuthorization(EventsPermissions.CheckIn);
    }

    internal sealed record RegisterRequest(Guid ContactId, Guid? TicketTypeId, string? Note);

    internal sealed record CheckInRequest(string Code);

    private static IResult NoContent(Result<Guid> result) => result.IsSuccess
        ? Results.NoContent()
        : result.Error == EventErrors.RegistrationNotFound ? Results.NotFound() : Problem(result.Error!);

    private static IResult Problem(Error error) =>
        Results.Problem(title: error.Message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
