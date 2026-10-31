using Arma.Modules.Comms.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Giving.Infrastructure;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Payments;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arma.Modules.Giving.Application;

public sealed record OnlineDonationRequest(string FirstName, string LastName, string Email, string? Phone, decimal Amount, Guid? FundId)
{
    /// <summary>Ödeme sağlayıcısının anahtarı; boşsa formda sunulan ilk sağlayıcı.</summary>
    public string? Provider { get; init; }

    /// <summary>Her ay kayıtlı karttan tekrarlansın.</summary>
    public bool Monthly { get; init; }
}

/// <summary>Çevrim içi bağış formunun adresleri; geri çağrı adresleri buradan üretilir.</summary>
public sealed record OnlineGivingUrls(string BaseUrl)
{
    public string Callback(string provider) => $"{BaseUrl}/api/v1/payments/{provider}/callback";

    public string Success(Guid donationId) => $"{BaseUrl}/bagis/tesekkurler?d={donationId:N}";

    public string Fail(Guid donationId) => $"{BaseUrl}/bagis/basarisiz?d={donationId:N}";
}

public sealed record OnlineDonationStart(Guid DonationId, string Provider, string? RedirectUrl, string? IframeUrl);

/// <summary>Bağış formunda sunulan ödeme seçeneği.</summary>
public sealed record PaymentOption(string Key, string DisplayName, string? Description, bool SupportsMonthly);

/// <summary>Yönetim ekranındaki satır: bulunan her sağlayıcı, yapılandırılıp yapılandırılmadığı ve formda sunulup sunulmadığı.</summary>
public sealed record PaymentProviderStatus(PaymentProviderInfo Info, bool IsOffered, int Position);

/// <summary>Kamuya açık bağış formu: bağışçıyı kaydeder, bekleyen bağışı açar ve ödemeyi başlatır.</summary>
public sealed class OnlineGivingService(
    GivingDbContext db,
    IContactRegistry registry,
    DonationService donations,
    IPaymentGateway payments,
    IDataProtectionProvider protection,
    TimeProvider clock,
    ILogger<OnlineGivingService> logger)
{
    public const decimal MinimumAmount = 1m;
    public const decimal MaximumAmount = 1_000_000m;

    internal const string CardProtectorPurpose = "Arma.Giving.StoredCards";

    /// <summary>Bağış formunda sunulan, yapılandırılmış sağlayıcılar (yönetim ekranındaki sırayla).</summary>
    public async Task<IReadOnlyList<PaymentOption>> OptionsAsync(CancellationToken cancellationToken = default) =>
        [.. (await ProvidersAsync(cancellationToken)).Where(p => p.IsOffered && p.Info.IsConfigured)
            .Select(p => new PaymentOption(p.Info.Key, p.Info.Descriptor.DisplayName, p.Info.Descriptor.Description,
                p.Info.Descriptor.Supports(PaymentCapabilities.StoredCard)))];

    /// <summary>Bulunan bütün sağlayıcılar. Seçim yapılmamışsa yapılandırılmış olanların hepsi sunulur.</summary>
    public async Task<IReadOnlyList<PaymentProviderStatus>> ProvidersAsync(CancellationToken cancellationToken = default)
    {
        var chosen = Parse(await db.ReceiptSettings.Select(s => s.PaymentProviders).FirstOrDefaultAsync(cancellationToken));
        var all = payments.Providers;
        if (chosen is null)
        {
            return [.. all.Select((p, i) => new PaymentProviderStatus(p, p.IsConfigured, i))];
        }

        return [.. all.Select(p => (Info: p, Index: Array.IndexOf(chosen, p.Key)))
            .OrderBy(p => p.Index < 0 ? int.MaxValue : p.Index)
            .Select((p, i) => new PaymentProviderStatus(p.Info, p.Index >= 0, i))];
    }

    /// <summary>Formda sunulacak sağlayıcıları sırasıyla kaydeder.</summary>
    public async Task<Result<bool>> SaveOfferedProvidersAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        var known = payments.Providers.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.FirstOrDefault(k => !known.Contains(k)) is { } unknown)
        {
            return new Error("online.provider_unknown", $"'{unknown}' adlı ödeme sağlayıcısı bu kurulumda yok.");
        }

        var settings = await db.ReceiptSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new ReceiptSettings();
            db.ReceiptSettings.Add(settings);
        }

        // Boş liste "hiçbiri" anlamına gelir; null ise "yapılandırılmış hepsi" (varsayılan) olarak kalır.
        settings.PaymentProviders = string.Join(',', keys.Distinct(StringComparer.OrdinalIgnoreCase).Select(k => k.ToLowerInvariant()));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<OnlineDonationStart>> StartAsync(OnlineDonationRequest request, string ip, OnlineGivingUrls urls, CancellationToken cancellationToken = default)
    {
        var options = await OptionsAsync(cancellationToken);
        if (options.Count == 0)
        {
            return new Error("online.disabled", "Çevrim içi bağış bu kurulumda kapalı.");
        }

        var option = string.IsNullOrEmpty(request.Provider) ? options[0] : options.FirstOrDefault(o => string.Equals(o.Key, request.Provider, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            return new Error("online.provider", "Seçilen ödeme yöntemi kullanılamıyor.");
        }

        if (request.Monthly && !option.SupportsMonthly)
        {
            return new Error("online.monthly", $"{option.DisplayName} ile düzenli bağış yapılamıyor; başka bir ödeme yöntemi seçin.");
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

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var created = Donation.StartOnline(contactId, request.Amount, request.FundId, today, option.Key);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        var donation = created.Value!;
        db.Donations.Add(donation);
        if (request.Monthly)
        {
            var recurring = RecurringDonation.Start(contactId, request.Amount, request.FundId, option.Key, today).Value!;
            db.RecurringDonations.Add(recurring);
            donation.AssignRecurring(recurring.Id);
        }

        await db.SaveChangesAsync(cancellationToken);

        var payment = PaymentRequestBuilder.For(option.Key)
            .Order(donation.Id)
            .Amount(donation.Amount, donation.Currency)
            .Description(request.Monthly ? "Düzenli bağış" : "Bağış")
            .Payer(new Payer(request.FirstName.Trim(), request.LastName.Trim(), request.Email.Trim(), request.Phone, ip))
            .ReturnTo(urls.Callback(option.Key), urls.Success(donation.Id), urls.Fail(donation.Id))
            .StoreCard(request.Monthly)
            .Build();

        var start = await payments.StartAsync(payment, cancellationToken);
        if (!start.IsSuccess)
        {
            donation.MarkFailed(start.Error);
            await AbandonRecurringAsync(donation, start.Error!, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return new Error("online.provider", start.Error!);
        }

        return new OnlineDonationStart(donation.Id, option.Key, start.RedirectUrl, start.IframeUrl);
    }

    /// <summary>Ödeme kuruluşunun bildirimini işler. Aynı bildirim tekrar gelirse sonuç değişmez.</summary>
    public async Task<PaymentNotification?> HandleCallbackAsync(string providerKey, IFormCollection form, CancellationToken cancellationToken = default)
    {
        var notification = await payments.VerifyCallbackAsync(providerKey, form, cancellationToken);
        if (notification is null || !Guid.TryParseExact(notification.OrderId, "N", out var donationId))
        {
            return null;
        }

        var donation = await db.Donations.FirstOrDefaultAsync(d => d.Id == donationId, cancellationToken);
        if (donation is null || !string.Equals(donation.Provider, providerKey, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Ödeme bildirimi bilinmeyen bağış için geldi: {Provider} {OrderId}", providerKey, notification.OrderId);
            return null;
        }

        if (!notification.Success)
        {
            donation.MarkFailed(notification.FailureReason);
            await AbandonRecurringAsync(donation, notification.FailureReason ?? "İlk ödeme başarısız", cancellationToken);
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

        var monthly = firstNotice && await ActivateRecurringAsync(donation, notification.Card, cancellationToken);

        // Teşekkür e-postası yalnızca ilk başarılı bildirimde gönderilir; tekrar gelen bildirim yeni e-posta üretmez.
        await donations.SaveWithTimelineAsync(donation, cancellationToken, firstNotice ? await ThankYouAsync(donation, monthly, cancellationToken) : null);
        return notification;
    }

    public IResult Acknowledge(string providerKey, PaymentNotification? notification, string successUrl, string failUrl) =>
        payments.Acknowledge(providerKey, notification, successUrl, failUrl);

    /// <summary>Ödeme sayfası bağış formunun içinde çerçevelenebilir mi.</summary>
    public async Task<bool> AllowsFrameAsync(Guid donationId, string url, CancellationToken cancellationToken = default)
    {
        var provider = await db.Donations.Where(d => d.Id == donationId && d.Status == DonationStatus.Pending).Select(d => d.Provider).FirstOrDefaultAsync(cancellationToken);
        return provider is not null && payments.AllowsFrame(provider, url);
    }

    public bool IsTestProviderAvailable => payments.Find("test")?.IsConfigured == true;

    public async Task<(DonationStatus Status, decimal Amount, bool Monthly)?> StatusAsync(Guid donationId, CancellationToken cancellationToken = default)
    {
        var row = await db.Donations.Where(d => d.Id == donationId && d.Method == PaymentMethod.Online)
            .Select(d => new
            {
                d.Status,
                d.Amount,
                Monthly = db.RecurringDonations.Any(r => r.Id == d.RecurringDonationId && r.Status == RecurringStatus.Active),
            })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.Status, row.Amount, row.Monthly);
    }

    private async Task<bool> ActivateRecurringAsync(Donation donation, StoredCard? card, CancellationToken cancellationToken)
    {
        if (donation.RecurringDonationId is not { } recurringId
            || await db.RecurringDonations.FirstOrDefaultAsync(r => r.Id == recurringId, cancellationToken) is not { } recurring)
        {
            return false;
        }

        if (card is null)
        {
            logger.LogWarning("Düzenli bağış için kart kaydedilmedi ({DonationId}); bağış tek seferlik olarak kaldı", donation.Id);
            recurring.AbandonSetup("Kart kaydedilmedi; bağış tek seferlik alındı.", clock.GetUtcNow());
            return false;
        }

        recurring.Activate(protection.CreateProtector(CardProtectorPurpose).Protect(card.Token), card.Label, donation.ReceivedOn);
        return recurring.Status == RecurringStatus.Active;
    }

    private async Task AbandonRecurringAsync(Donation donation, string reason, CancellationToken cancellationToken)
    {
        if (donation.RecurringDonationId is { } recurringId
            && await db.RecurringDonations.FirstOrDefaultAsync(r => r.Id == recurringId, cancellationToken) is { } recurring)
        {
            recurring.AbandonSetup(reason, clock.GetUtcNow());
        }
    }

    private async Task<SendTransactionalEmail> ThankYouAsync(Donation donation, bool monthly, CancellationToken cancellationToken)
    {
        var organization = await db.ReceiptSettings.Select(r => r.OrganizationName).FirstOrDefaultAsync(cancellationToken);
        var signature = string.IsNullOrWhiteSpace(organization) ? "" : $"\n\n{organization}";
        var schedule = monthly ? "\n\nDüzenli bağışınız her ay aynı gün kayıtlı kartınızdan alınacak. Dilediğiniz zaman bağışçı portalından durdurabilirsiniz." : "";
        return new SendTransactionalEmail(donation.ContactId, "Bağışınız için teşekkür ederiz",
            $"Sayın {{{{adsoyad}}}},\n\n{Money.Format(donation.Amount, donation.Currency)} tutarındaki bağışınız bize ulaştı. Desteğiniz için teşekkür ederiz.{schedule}{signature}");
    }

    private static string[]? Parse(string? value) =>
        value is null ? null : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
