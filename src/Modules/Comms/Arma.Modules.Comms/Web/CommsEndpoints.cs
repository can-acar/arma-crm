using Arma.Modules.Comms.Application;
using Arma.Modules.Comms.Domain;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Comms.Web;

internal static class CommsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var comms = endpoints.MapGroup("/api/v1/comms").WithTags("İletişim").RequireAuthorization(CommsPermissions.Read);

        comms.MapGet("/contacts/{contactId:guid}/consents", (ConsentService service, Guid contactId, CancellationToken ct) => service.ForContactAsync(contactId, ct));

        comms.MapPut("/contacts/{contactId:guid}/consents/{channel}", async (ConsentService service, Guid contactId, Channel channel, ConsentRequest request, CancellationToken ct) =>
                Respond(await service.SetAsync(contactId, channel, request.Status, string.IsNullOrWhiteSpace(request.Source) ? "api" : request.Source.Trim(), ct)))
            .RequireAuthorization(CommsPermissions.Consent);

        comms.MapGet("/contacts/{contactId:guid}/messages", (MessageService service, Guid contactId, CancellationToken ct) => service.ForContactAsync(contactId, 100, ct));

        comms.MapPost("/messages", async (MessageService service, SendToContactRequest request, CancellationToken ct) =>
            {
                var result = await service.SendToContactAsync(request, ct);
                return result.IsSuccess ? Results.Accepted(value: new { id = result.Value }) : Problem(result.Error!);
            })
            .RequireAuthorization(CommsPermissions.Send);

        var mailings = comms.MapGroup("/mailings").RequireAuthorization(CommsPermissions.Mailings);
        mailings.MapGet("/", (MailingService service, CancellationToken ct) => service.ListAsync(ct));
        mailings.MapGet("/{id:guid}", async (MailingService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } mailing ? Results.Ok(mailing) : Results.NotFound());
        mailings.MapPost("/", async (MailingService service, SaveMailingRequest request, CancellationToken ct) =>
        {
            var result = await service.SaveAsync(null, request, ct);
            return result.IsSuccess ? Results.Created($"/api/v1/comms/mailings/{result.Value}", new { id = result.Value }) : Problem(result.Error!);
        });
        mailings.MapPut("/{id:guid}", async (MailingService service, Guid id, SaveMailingRequest request, CancellationToken ct) =>
            Respond(await service.SaveAsync(id, request, ct)));
        mailings.MapPost("/{id:guid}/start", async (MailingService service, Guid id, CancellationToken ct) =>
            Respond(await service.StartAsync(id, ct)));

        // Abonelik iptali: e-postadaki List-Unsubscribe başlığı (tek tık, RFC 8058) ve iptal sayfasındaki form buraya gelir.
        // Oturum yoktur; istek, yalnızca o kişi ve kanal için üretilmiş imzalı belirteçle doğrulanır.
        endpoints.MapGet("/api/v1/unsubscribe", (string? t) => Results.Redirect($"/abonelik/iptal?t={Uri.EscapeDataString(t ?? "")}"))
            .AllowAnonymous().ExcludeFromDescription();
        endpoints.MapPost("/api/v1/unsubscribe", async (HttpRequest request, ConsentService consents, string? t, CancellationToken ct) =>
            {
                var oneClick = request.HasFormContentType && (await request.ReadFormAsync(ct))["List-Unsubscribe"] == "One-Click";
                var done = await consents.UnsubscribeAsync(t, ct);
                return oneClick
                    ? (done ? Results.Ok() : Results.BadRequest())
                    : Results.Redirect(done ? "/abonelik/iptal/tamam" : "/abonelik/iptal?hata=1");
            })
            .AllowAnonymous().DisableAntiforgery().ExcludeFromDescription();
    }

    internal sealed record ConsentRequest(ConsentStatus Status, string? Source);

    private static IResult Respond(Result<Guid> result) => result.IsSuccess
        ? Results.NoContent()
        : result.Error!.Code.EndsWith("not_found", StringComparison.Ordinal) ? Results.NotFound() : Problem(result.Error!);

    private static IResult Problem(Error error) =>
        Results.Problem(title: error.Message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
