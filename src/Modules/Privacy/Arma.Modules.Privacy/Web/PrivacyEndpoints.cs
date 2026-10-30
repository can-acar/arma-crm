using Arma.Modules.Privacy.Application;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Privacy.Web;

internal static class PrivacyEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var privacy = endpoints.MapGroup("/api/v1/privacy").WithTags("KVKK").RequireAuthorization(PrivacyPermissions.Read);

        privacy.MapGet("/inventory", (PersonalDataService service) => service.Inventory());
        privacy.MapGet("/inventory.csv", (PersonalDataService service) =>
            Results.File(service.InventoryCsv(), "text/csv; charset=utf-8", "kisisel-veri-envanteri.csv"));

        privacy.MapGet("/contacts/{contactId:guid}/consents", (ConsentRecordService service, Guid contactId, CancellationToken ct) => service.ForContactAsync(contactId, ct));
        privacy.MapPost("/contacts/{contactId:guid}/consents", async (ConsentRecordService service, HttpContext http, Guid contactId, RecordConsentRequest request, CancellationToken ct) =>
                Respond(await service.RecordAsync(contactId, request.NoticeId, request.Granted, "api", http.Connection.RemoteIpAddress?.ToString(), request.Note, ct)))
            .RequireAuthorization(PrivacyPermissions.Manage);

        privacy.MapGet("/contacts/{contactId:guid}/export", async (PersonalDataService service, Guid contactId, CancellationToken ct) =>
            {
                var result = await service.ExportAsync(contactId, ct);
                return result.IsSuccess ? Results.File(result.Value!, "application/json; charset=utf-8", $"kisisel-veriler-{contactId:N}.json") : Results.NotFound();
            })
            .RequireAuthorization(PrivacyPermissions.Export);

        privacy.MapPost("/contacts/{contactId:guid}/anonymize", async (PersonalDataService service, Guid contactId, AnonymizeRequest request, CancellationToken ct) =>
                Respond(await service.AnonymizeAsync(contactId, request.RequestId, ct)))
            .RequireAuthorization(PrivacyPermissions.Erase);

        privacy.MapGet("/requests", (DataRequestService service, bool openOnly = false, CancellationToken ct = default) => service.ListAsync(openOnly, null, ct));
        privacy.MapGet("/requests/{id:guid}", async (DataRequestService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } request ? Results.Ok(request) : Results.NotFound());
        privacy.MapPost("/requests", async (DataRequestService service, CreateDataRequest request, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(request, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/privacy/requests/{result.Value}", new { id = result.Value }) : Problem(result.Error!);
            })
            .RequireAuthorization(PrivacyPermissions.Manage);
    }

    internal sealed record RecordConsentRequest(Guid NoticeId, bool Granted, string? Note);

    internal sealed record AnonymizeRequest(Guid? RequestId);

    private static IResult Respond(Result<Guid> result) => result.IsSuccess
        ? Results.NoContent()
        : result.Error!.Code.EndsWith("not_found", StringComparison.Ordinal) ? Results.NotFound() : Problem(result.Error!);

    private static IResult Problem(Error error) =>
        Results.Problem(title: error.Message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
