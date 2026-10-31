namespace Arma.SharedKernel.Payments;

/// <summary>
/// Ödeme isteğini adım adım kurar (kurucu). <see cref="Build"/> eksik veya hatalı alanları tek seferde bildirir:
/// <code>
/// var request = PaymentRequestBuilder.For("iyzico")
///     .Order(id).Amount(150m).Description("Bağış")
///     .Payer(payer).ReturnTo(callback, success, fail)
///     .StoreCard()
///     .Build();
/// </code>
/// </summary>
public sealed class PaymentRequestBuilder
{
    private readonly string providerKey;
    private string? orderId, description, callbackUrl, successUrl, failUrl;
    private string currency = "TRY", category = "Bağış";
    private decimal? amount;
    private Payer? payer;
    private bool storeCard;

    private PaymentRequestBuilder(string providerKey) => this.providerKey = providerKey;

    public static PaymentRequestBuilder For(string providerKey) => new(providerKey);

    public PaymentRequestBuilder Order(string value)
    {
        orderId = value;
        return this;
    }

    public PaymentRequestBuilder Order(Guid value) => Order(value.ToString("N"));

    public PaymentRequestBuilder Amount(decimal value, string currencyCode = "TRY")
    {
        amount = value;
        currency = currencyCode;
        return this;
    }

    public PaymentRequestBuilder Description(string value, string? itemCategory = null)
    {
        description = value;
        category = itemCategory ?? category;
        return this;
    }

    public PaymentRequestBuilder Payer(Payer value)
    {
        payer = value;
        return this;
    }

    public PaymentRequestBuilder ReturnTo(string callback, string success, string fail)
    {
        (callbackUrl, successUrl, failUrl) = (callback, success, fail);
        return this;
    }

    public PaymentRequestBuilder StoreCard(bool value = true)
    {
        storeCard = value;
        return this;
    }

    /// <exception cref="InvalidOperationException">Zorunlu bir alan eksik veya hatalıysa.</exception>
    public PaymentRequest Build()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            problems.Add("sağlayıcı");
        }

        if (string.IsNullOrEmpty(orderId) || !orderId.All(char.IsAsciiLetterOrDigit))
        {
            problems.Add("sipariş numarası (yalnızca harf ve rakam)");
        }

        if (amount is not > 0m || decimal.Round(amount.Value, 2) != amount)
        {
            problems.Add("tutar (sıfırdan büyük, en çok iki ondalık)");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            problems.Add("açıklama");
        }

        if (payer is null || string.IsNullOrWhiteSpace(payer.Email) || string.IsNullOrWhiteSpace(payer.Ip))
        {
            problems.Add("ödeyen (e-posta ve IP)");
        }

        if (!IsAbsolute(callbackUrl) || !IsAbsolute(successUrl) || !IsAbsolute(failUrl))
        {
            problems.Add("dönüş adresleri (tam adres)");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Ödeme isteği eksik: " + string.Join(", ", problems) + ".");
        }

        return new PaymentRequest
        {
            ProviderKey = providerKey,
            OrderId = orderId!,
            Amount = amount!.Value,
            Currency = currency,
            Description = description!,
            Category = category,
            Payer = payer!,
            CallbackUrl = callbackUrl!,
            SuccessUrl = successUrl!,
            FailUrl = failUrl!,
            StoreCard = storeCard,
        };
    }

    private static bool IsAbsolute(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
