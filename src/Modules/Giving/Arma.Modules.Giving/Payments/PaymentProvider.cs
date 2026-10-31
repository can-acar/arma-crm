using Microsoft.AspNetCore.Http;

namespace Arma.Modules.Giving.Payments;

public sealed record Payer(string FirstName, string LastName, string Email, string? Phone, string Ip);

/// <summary>Ödeme sayfasını açmak için gereken bilgiler. <see cref="OrderId"/> yalnızca harf ve rakamdır.</summary>
public sealed record PaymentRequest(string OrderId, decimal Amount, string Description, Payer Payer, string CallbackUrl, string SuccessUrl, string FailUrl);

/// <summary>
/// Ödeme başlatma sonucu: kullanıcı <see cref="RedirectUrl"/> adresine gönderilir ya da
/// <see cref="IframeUrl"/> sayfa içinde gösterilir.
/// </summary>
public sealed record PaymentStart(string? RedirectUrl, string? IframeUrl, string? Error)
{
    public static PaymentStart Failed(string error) => new(null, null, error);
}

/// <summary>Ödeme kuruluşunun bildirdiği, imzası doğrulanmış sonuç.</summary>
public sealed record PaymentNotification(string OrderId, bool Success, decimal PaidAmount, string ProviderPaymentId, string? FailureReason);

/// <summary>Çevrim içi bağış için ödeme kuruluşu. Kurulumda yapılandırılan tek sağlayıcı kullanılır.</summary>
public interface IPaymentProvider
{
    /// <summary>Adres ve kayıtlarda kullanılan kısa ad (iyzico, paytr).</summary>
    string Name { get; }

    Task<PaymentStart> StartAsync(PaymentRequest request, CancellationToken cancellationToken);

    /// <summary>Geri çağrıyı doğrular; imza veya sorgu geçersizse null döner.</summary>
    Task<PaymentNotification?> VerifyCallbackAsync(IFormCollection form, CancellationToken cancellationToken);

    /// <summary>
    /// Geri çağrıya verilecek yanıt. Tarayıcıdan gelen geri çağrıda (iyzico) kullanıcı sonuç sayfasına
    /// yönlendirilir; sunucudan gelen bildirimde (PayTR) "OK" döner.
    /// </summary>
    IResult Acknowledge(PaymentNotification? notification, string successUrl, string failUrl);
}

public sealed class PaymentOptions
{
    public const string Section = "Giving:Payments";

    /// <summary>none, iyzico, paytr veya (yalnızca geliştirme ve testte) test.</summary>
    public string Provider { get; set; } = "none";

    public IyzicoOptions Iyzico { get; set; } = new();

    public PayTrOptions PayTR { get; set; } = new();
}

public sealed class IyzicoOptions
{
    public string ApiKey { get; set; } = "";

    public string SecretKey { get; set; } = "";

    /// <summary>Canlı: https://api.iyzipay.com, deneme: https://sandbox-api.iyzipay.com</summary>
    public string BaseUrl { get; set; } = "https://sandbox-api.iyzipay.com";
}

public sealed class PayTrOptions
{
    public string MerchantId { get; set; } = "";

    public string MerchantKey { get; set; } = "";

    public string MerchantSalt { get; set; } = "";

    /// <summary>Açıkken PayTR test kartlarıyla işlem yapılır, para çekilmez.</summary>
    public bool TestMode { get; set; } = true;
}
