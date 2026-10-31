namespace Arma.SharedKernel.Payments;

public sealed record Payer(string FirstName, string LastName, string Email, string? Phone, string Ip)
{
    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>Ödeme sayfasını açmak için gereken bilgiler; <see cref="PaymentRequestBuilder"/> ile kurulur.</summary>
public sealed record PaymentRequest
{
    internal PaymentRequest()
    {
    }

    public required string ProviderKey { get; init; }

    /// <summary>Yalnızca harf ve rakamdan oluşan sipariş numarası; geri çağrıda aynen döner.</summary>
    public required string OrderId { get; init; }

    public required decimal Amount { get; init; }

    public string Currency { get; init; } = "TRY";

    public required string Description { get; init; }

    public string Category { get; init; } = "Bağış";

    public required Payer Payer { get; init; }

    public required string CallbackUrl { get; init; }

    public required string SuccessUrl { get; init; }

    public required string FailUrl { get; init; }

    /// <summary>Kart sağlayıcıda saklansın; geri çağrıda <see cref="PaymentNotification.Card"/> döner.</summary>
    public bool StoreCard { get; init; }
}

/// <summary>
/// Ödeme başlatma sonucu: kullanıcı <see cref="RedirectUrl"/> adresine gönderilir ya da
/// <see cref="IframeUrl"/> sayfa içinde gösterilir.
/// </summary>
public sealed record PaymentStart(string? RedirectUrl, string? IframeUrl, string? Error)
{
    public bool IsSuccess => Error is null;

    public static PaymentStart Redirect(string url) => new(url, null, null);

    public static PaymentStart Frame(string url) => new(null, url, null);

    public static PaymentStart Failed(string error) => new(null, null, error);
}

/// <summary>Sağlayıcıda saklanan kart. <see cref="Token"/> sağlayıcıya özeldir ve gizli tutulmalıdır.</summary>
public sealed record StoredCard(string Token, string? Label);

/// <summary>Ödeme kuruluşunun bildirdiği, imzası doğrulanmış sonuç.</summary>
public sealed record PaymentNotification(string OrderId, bool Success, decimal PaidAmount, string ProviderPaymentId, string? FailureReason, StoredCard? Card = null)
{
    public string ProviderKey { get; init; } = "";
}

/// <summary>Kayıtlı karttan tahsilat isteği.</summary>
public sealed record StoredCardCharge(string ProviderKey, string OrderId, decimal Amount, string Currency, string Description, Payer Payer, StoredCard Card);

public sealed record PaymentResult(bool Success, string? ProviderPaymentId, string? Error)
{
    public static PaymentResult Paid(string providerPaymentId) => new(true, providerPaymentId, null);

    public static PaymentResult Failed(string error) => new(false, null, error);
}
