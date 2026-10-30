using Arma.Modules.Comms.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Giving.Infrastructure;
using Arma.Modules.Giving.Payments;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arma.Modules.Giving.Application;

public sealed record OnlineDonationRequest(string FirstName, string LastName, string Email, string? Phone, decimal Amount, Guid? FundId);

/// <summary>Çevrim içi bağış formunun adresleri; geri çağrı adresleri buradan üretilir.</summary>
public sealed record OnlineGivingUrls(string BaseUrl)
{
    public string Callback(string provider) => $"{BaseUrl}/api/v1/payments/{provider}/callback";

    public string Success(Guid donationId) => $"{BaseUrl}/bagis/tesekkurler?d={donationId:N}";

    public string Fail(Guid donationId) => $"{BaseUrl}/bagis/basarisiz?d={donationId:N}";
}

public sealed record OnlineDonationStart(Guid DonationId, string? RedirectUrl, string? IframeUrl);

/// <summary>Kamuya açık bağış formu: bağışçıyı kaydeder, bekleyen bağışı açar ve ödemeyi başlatır.</summary>
public sealed class OnlineGivingService(
    GivingDbContext db,
    IContactRegistry registry,
    DonationService donations,
    TimeProvider clock,
    ILogger<OnlineGivingService> logger,
    IPaymentProvider? provider = null)
{
    public const decimal MinimumAmount = 1m;
    public const decimal MaximumAmount = 1_000_000m;

    public bool IsEnabled => provider is not null;

    public bool IsTestProvider => provider is TestPaymentProvider;

    public async Task<Result<OnlineDonationStart>> StartAsync(OnlineDonationRequest request, string ip, OnlineGivingUrls urls, CancellationToken cancellationToken = default)
    {
        if (provider is null)
        {
            return new Error("online.disabled", "Çevrim içi bağış bu kurulumda kapalı.");
        }

        if (request.Amount is < MinimumAmount or > MaximumAmount || decimal.Round(request.Amount, 2) != request.Amount)
        {
            return new Error("online.amount", $"Tutar {MinimumAmount:N0} ile {MaximumAmount:N0} TL arasında olmalı.");
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            return new Error("online.email", "Geçerli bir e-posta adresi gerekli; makbuz bu adrese gönderilir.");
        }

        if (request.FundId is { } fundId && !await db.Funds.AnyAsync(f => f.Id == fundId && f.IsActive && f.IsPublic, cancellationToken))
        {
            return DonationErrors.FundNotFound;
        }

        var contact = await registry.FindOrCreatePersonAsync(request.FirstName, request.LastName, request.Email, request.Phone, cancellationToken);
        if (contact.Id is not { } contactId)
        {
            return new Error("online.contact", contact.Error ?? "Bağışçı bilgileri kaydedilemedi.");
        }

        var created = Donation.StartOnline(contactId, request.Amount, request.FundId, DateOnly.FromDateTime(clock.GetLocalNow().DateTime), provider.Name);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        var donation = created.Value!;
        db.Donations.Add(donation);
        await db.SaveChangesAsync(cancellationToken);

        var start = await provider.StartAsync(new PaymentRequest(
            donation.Id.ToString("N"), donation.Amount, "Bağış",
            new Payer(request.FirstName.Trim(), request.LastName.Trim(), request.Email.Trim(), request.Phone, ip),
            urls.Callback(provider.Name), urls.Success(donation.Id), urls.Fail(donation.Id)), cancellationToken);
        if (start.Error is not null)
        {
            donation.MarkFailed(start.Error);
            await db.SaveChangesAsync(cancellationToken);
            return new Error("online.provider", start.Error);
        }

        return new OnlineDonationStart(donation.Id, start.RedirectUrl, start.IframeUrl);
    }

    /// <summary>Ödeme kuruluşunun bildirimini işler. Aynı bildirim tekrar gelirse sonuç değişmez.</summary>
    public async Task<PaymentNotification?> HandleCallbackAsync(string providerName, Microsoft.AspNetCore.Http.IFormCollection form, CancellationToken cancellationToken = default)
    {
        if (provider is null || provider.Name != providerName)
        {
            return null;
        }

        var notification = await provider.VerifyCallbackAsync(form, cancellationToken);
        if (notification is null || !Guid.TryParseExact(notification.OrderId, "N", out var donationId))
        {
            return null;
        }

        var donation = await db.Donations.FirstOrDefaultAsync(d => d.Id == donationId, cancellationToken);
        if (donation is null)
        {
            logger.LogWarning("Ödeme bildirimi bilinmeyen bağış için geldi: {OrderId}", notification.OrderId);
            return null;
        }

        if (!notification.Success)
        {
            donation.MarkFailed(notification.FailureReason);
            await db.SaveChangesAsync(cancellationToken);
            return notification;
        }

        var firstNotice = donation.Status == DonationStatus.Pending;
        var paid = donation.MarkPaid(notification.ProviderPaymentId, notification.PaidAmount);
        if (!paid.IsSuccess)
        {
            logger.LogError("Ödeme bildirimi işlenemedi ({DonationId}): {Error}", donation.Id, paid.Error!.Message);
            return notification with { Success = false, FailureReason = paid.Error.Message };
        }

        // Teşekkür e-postası yalnızca ilk başarılı bildirimde gönderilir; tekrar gelen bildirim yeni e-posta üretmez.
        await donations.SaveWithTimelineAsync(donation, cancellationToken, firstNotice ? await ThankYouAsync(donation, cancellationToken) : null);
        return notification;
    }

    private async Task<SendTransactionalEmail> ThankYouAsync(Donation donation, CancellationToken cancellationToken)
    {
        var organization = await db.ReceiptSettings.Select(r => r.OrganizationName).FirstOrDefaultAsync(cancellationToken);
        var signature = string.IsNullOrWhiteSpace(organization) ? "" : $"\n\n{organization}";
        return new SendTransactionalEmail(donation.ContactId, "Bağışınız için teşekkür ederiz",
            $"Sayın {{{{adsoyad}}}},\n\n{Money.Format(donation.Amount, donation.Currency)} tutarındaki bağışınız bize ulaştı. Desteğiniz için teşekkür ederiz.{signature}");
    }

    public Microsoft.AspNetCore.Http.IResult Acknowledge(PaymentNotification? notification, string successUrl, string failUrl) =>
        provider?.Acknowledge(notification, successUrl, failUrl) ?? Microsoft.AspNetCore.Http.Results.NotFound();

    public async Task<(DonationStatus Status, decimal Amount)?> StatusAsync(Guid donationId, CancellationToken cancellationToken = default)
    {
        var row = await db.Donations.Where(d => d.Id == donationId && d.Method == PaymentMethod.Online)
            .Select(d => new { d.Status, d.Amount })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.Status, row.Amount);
    }
}
