using Arma.Modules.Giving.Application;
using Arma.Modules.Giving.Domain;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Giving.Web;

internal static class GivingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var donations = endpoints.MapGroup("/api/v1/donations").WithTags("Bağışlar").RequireAuthorization(GivingPermissions.Read);

        donations.MapGet("/", (DonationService service, Guid? contactId, Guid? fundId, PaymentMethod? method, DonationStatus? status, DateOnly? from, DateOnly? to, string? search, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            service.ListAsync(new DonationFilter(contactId, fundId, method, status, from, to, search, page, pageSize), ct));

        donations.MapGet("/{id:guid}", async (DonationService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } donation ? Results.Ok(donation) : Results.NotFound());

        donations.MapPost("/", async (DonationService service, SaveDonationRequest request, CancellationToken ct) =>
            {
                var result = await service.RecordAsync(request, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/donations/{result.Value}", new { id = result.Value }) : Problem(result.Error!);
            })
            .RequireAuthorization(GivingPermissions.Write);

        donations.MapPut("/{id:guid}", async (DonationService service, Guid id, SaveDonationRequest request, CancellationToken ct) =>
                NoContent(await service.UpdateAsync(id, request, ct)))
            .RequireAuthorization(GivingPermissions.Write);

        donations.MapPost("/{id:guid}/refund", async (DonationService service, Guid id, ReasonRequest request, CancellationToken ct) =>
                NoContent(await service.RefundAsync(id, request.Reason, ct)))
            .RequireAuthorization(GivingPermissions.Write);

        donations.MapDelete("/{id:guid}", async (DonationService service, Guid id, CancellationToken ct) =>
                NoContent(await service.DeleteAsync(id, ct)))
            .RequireAuthorization(GivingPermissions.Delete);

        donations.MapPost("/{id:guid}/receipt", async (DonationService service, Guid id, CancellationToken ct) =>
            {
                var result = await service.IssueReceiptAsync(id, ct);
                return result.IsSuccess ? Results.Ok(new { number = result.Value }) : result.Error == DonationErrors.NotFound ? Results.NotFound() : Problem(result.Error!);
            })
            .RequireAuthorization(GivingPermissions.Receipts);

        donations.MapPost("/{id:guid}/receipt/void", async (DonationService service, Guid id, ReasonRequest request, CancellationToken ct) =>
                NoContent(await service.VoidReceiptAsync(id, request.Reason ?? "", ct)))
            .RequireAuthorization(GivingPermissions.Receipts);

        endpoints.MapGet("/api/v1/funds", (FundService service, bool activeOnly = false, CancellationToken ct = default) => service.ListAsync(activeOnly, ct))
            .WithTags("Bağışlar").RequireAuthorization(GivingPermissions.Read);

        var recurring = endpoints.MapGroup("/api/v1/recurring-donations").WithTags("Bağışlar");
        recurring.MapGet("/", (RecurringDonationService service, RecurringStatus? status, Guid? contactId, CancellationToken ct) => service.ListAsync(status, contactId, ct))
            .RequireAuthorization(GivingPermissions.Read);
        recurring.MapPost("/{id:guid}/cancel", async (RecurringDonationService service, Guid id, CancellationToken ct) =>
                NoContent(await service.CancelAsync(id, null, ct)))
            .RequireAuthorization(GivingPermissions.Write);
        recurring.MapPost("/{id:guid}/resume", async (RecurringDonationService service, Guid id, CancellationToken ct) =>
                NoContent(await service.ResumeAsync(id, ct)))
            .RequireAuthorization(GivingPermissions.Write);

        endpoints.MapGet("/api/v1/payment-providers", (OnlineGivingService service, CancellationToken ct) => service.ProvidersAsync(ct))
            .WithTags("Ödemeler").RequireAuthorization(GivingPermissions.Settings);

        // Ödeme kuruluşlarının geri çağrısı: oturum yoktur, istek imza veya sorguyla doğrulanır.
        endpoints.MapPost("/api/v1/payments/{provider}/callback", async (HttpRequest request, OnlineGivingService online, string provider, CancellationToken ct) =>
            {
                var form = await request.ReadFormAsync(ct);
                var notification = await online.HandleCallbackAsync(provider, form, ct);
                var urls = new OnlineGivingUrls($"{request.Scheme}://{request.Host}{request.PathBase}");
                var donationId = notification is not null && Guid.TryParseExact(notification.OrderId, "N", out var id) ? id : Guid.Empty;
                return online.Acknowledge(provider, notification, urls.Success(donationId), urls.Fail(donationId));
            })
            .WithTags("Ödemeler").AllowAnonymous().DisableAntiforgery().ExcludeFromDescription();
    }

    internal sealed record ReasonRequest(string? Reason);

    private static IResult NoContent(Result<Guid> result) => result.IsSuccess
        ? Results.NoContent()
        : result.Error == DonationErrors.NotFound ? Results.NotFound() : Problem(result.Error!);

    private static IResult Problem(Error error) =>
        Results.Problem(title: error.Message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
