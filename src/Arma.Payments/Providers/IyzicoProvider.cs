using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arma.SharedKernel.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Arma.Payments.Providers;

/// <summary>
/// iyzico Ödeme Formu (Checkout Form). Kullanıcı iyzico sayfasında öder, tarayıcı token ile geri çağrı adresine
/// döner; sonuç token ile iyzico'dan sorgulanır (geri çağrıdaki verilere güvenilmez). Kart saklama açık üye
/// işyerlerinde, kullanıcı kartını kaydederse sonraki aylar kayıtlı karttan çekilir.
/// </summary>
public sealed class IyzicoProvider(IHttpClientFactory httpClients, ILogger<IyzicoProvider> logger) : IStoredCardPaymentProvider
{
    internal const string InitializePath = "/payment/iyzipos/checkoutform/initialize/auth/ecom";
    internal const string DetailPath = "/payment/iyzipos/checkoutform/auth/ecom/detail";
    internal const string ChargePath = "/payment/auth";

    // iyzico alıcının kimlik numarasını zorunlu tutar; bağış formunda istenmediğinde belgelerdeki yer tutucu gönderilir.
    private const string PlaceholderIdentityNumber = "11111111111";

    public PaymentProviderDescriptor Descriptor { get; } = new("iyzico", "iyzico", PaymentCapabilities.OneTime | PaymentCapabilities.StoredCard,
    [
        new("ApiKey", "API anahtarı"),
        new("SecretKey", "Gizli anahtar"),
        new("BaseUrl", "API adresi", IsSecret: false, DefaultValue: "https://sandbox-api.iyzipay.com",
            Help: "Canlı: https://api.iyzipay.com, deneme: https://sandbox-api.iyzipay.com"),
    ])
    {
        Description = "Kartla ödeme iyzico'nun güvenli sayfasında alınır. Kart saklama açıksa düzenli bağış yapılabilir.",
        Website = "https://www.iyzico.com",
        Order = 10,
    };

    public async Task<PaymentStart> StartAsync(PaymentRequest request, IPaymentProviderSettings settings, CancellationToken cancellationToken)
    {
        var price = Price(request.Amount);
        var body = Order(request.OrderId, price, request.Currency, request.Description, request.Category, request.Payer);
        body["callbackUrl"] = request.CallbackUrl;
        body["enabledInstallments"] = new JsonArray(1);

        var response = await SendAsync(settings, InitializePath, body, cancellationToken);
        if (response?["status"]?.GetValue<string>() == "success" && response["paymentPageUrl"]?.GetValue<string>() is { } url)
        {
            return PaymentStart.Redirect(url);
        }

        logger.LogWarning("iyzico ödeme formu açılamadı: {Error}", response?["errorMessage"]?.GetValue<string>());
        return PaymentStart.Failed("Ödeme sayfası açılamadı. Lütfen daha sonra tekrar deneyin.");
    }

    public async Task<PaymentNotification?> VerifyCallbackAsync(IFormCollection form, IPaymentProviderSettings settings, CancellationToken cancellationToken)
    {
        var token = form["token"].ToString();
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var detail = await SendAsync(settings, DetailPath, new JsonObject { ["locale"] = "tr", ["token"] = token }, cancellationToken);
        if (detail?["status"]?.GetValue<string>() != "success" || detail["basketId"]?.GetValue<string>() is not { } orderId)
        {
            logger.LogWarning("iyzico ödeme sonucu sorgulanamadı: {Error}", detail?["errorMessage"]?.GetValue<string>());
            return null;
        }

        var success = detail["paymentStatus"]?.GetValue<string>() == "SUCCESS";
        var paid = detail["price"] is { } p ? p.GetValue<decimal>() : 0m;
        var paymentId = detail["paymentId"]?.ToString() ?? token;
        return new PaymentNotification(orderId, success, paid, paymentId, success ? null : detail["errorMessage"]?.GetValue<string>(),
            success ? Card(detail) : null);
    }

    public IResult Acknowledge(PaymentNotification? notification, string successUrl, string failUrl) =>
        Results.Redirect(notification?.Success == true ? successUrl : failUrl);

    /// <summary>Kayıtlı karttan 3D'siz tahsilat (iyzico'da üye işyeri için kart saklama ve 3D'siz ödeme yetkisi gerekir).</summary>
    public async Task<PaymentResult> ChargeAsync(StoredCardCharge charge, IPaymentProviderSettings settings, CancellationToken cancellationToken)
    {
        if (charge.Card.Token.Split(':') is not [var userKey, var cardToken] || userKey.Length == 0 || cardToken.Length == 0)
        {
            return PaymentResult.Failed("Kayıtlı kart bilgisi geçersiz.");
        }

        var body = Order(charge.OrderId, Price(charge.Amount), charge.Currency, charge.Description, "Bağış", charge.Payer);
        body["installment"] = 1;
        body["paymentChannel"] = "WEB";
        body["paymentCard"] = new JsonObject { ["cardUserKey"] = userKey, ["cardToken"] = cardToken };

        var response = await SendAsync(settings, ChargePath, body, cancellationToken);
        if (response?["status"]?.GetValue<string>() == "success" && response["paymentId"]?.ToString() is { Length: > 0 } paymentId)
        {
            return PaymentResult.Paid(paymentId);
        }

        var error = response?["errorMessage"]?.GetValue<string>();
        logger.LogWarning("iyzico kayıtlı karttan tahsilat yapılamadı ({OrderId}): {Error}", charge.OrderId, error);
        return PaymentResult.Failed(error ?? "Kayıtlı karttan tahsilat yapılamadı.");
    }

    internal static StoredCard? Card(JsonNode detail)
    {
        if (detail["cardUserKey"]?.GetValue<string>() is not { Length: > 0 } userKey || detail["cardToken"]?.GetValue<string>() is not { Length: > 0 } cardToken)
        {
            return null;
        }

        var last4 = detail["lastFourDigits"]?.GetValue<string>();
        var brand = detail["cardAssociation"]?.GetValue<string>();
        var label = last4 is null ? null : $"{brand?.Replace('_', ' ')} **** {last4}".Trim();
        return new StoredCard($"{userKey}:{cardToken}", label);
    }

    private static string Price(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static JsonObject Order(string orderId, string price, string currency, string description, string category, Payer payer) => new()
    {
        ["locale"] = "tr",
        ["conversationId"] = orderId,
        ["price"] = price,
        ["paidPrice"] = price,
        ["currency"] = currency,
        ["basketId"] = orderId,
        ["paymentGroup"] = "PRODUCT",
        ["buyer"] = new JsonObject
        {
            ["id"] = orderId,
            ["name"] = payer.FirstName,
            ["surname"] = payer.LastName,
            ["identityNumber"] = PlaceholderIdentityNumber,
            ["email"] = payer.Email,
            ["gsmNumber"] = payer.Phone,
            ["registrationAddress"] = "-",
            ["city"] = "-",
            ["country"] = "Turkey",
            ["ip"] = payer.Ip,
        },
        ["billingAddress"] = new JsonObject
        {
            ["contactName"] = payer.FullName,
            ["city"] = "-",
            ["country"] = "Turkey",
            ["address"] = "-",
        },
        ["basketItems"] = new JsonArray(new JsonObject
        {
            ["id"] = orderId,
            ["name"] = description,
            ["category1"] = category,
            ["itemType"] = "VIRTUAL",
            ["price"] = price,
        }),
    };

    private async Task<JsonNode?> SendAsync(IPaymentProviderSettings settings, string path, JsonObject body, CancellationToken cancellationToken)
    {
        var json = body.ToJsonString();
        var random = Guid.NewGuid().ToString("N");
        using var message = new HttpRequestMessage(HttpMethod.Post, settings["BaseUrl"].TrimEnd('/') + path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("IYZWSv2", Authorization(settings, random, path, json));
        message.Headers.Add("x-iyzi-rnd", random);

        try
        {
            using var response = await httpClients.CreateClient(PaymentsBuilder.HttpClientName).SendAsync(message, cancellationToken);
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "iyzico'ya bağlanılamadı");
            return null;
        }
    }

    /// <summary>IYZWSv2: base64("apiKey:..&amp;randomKey:..&amp;signature:" + hex(HMACSHA256(secret, random + path + body))).</summary>
    internal static string Authorization(IPaymentProviderSettings settings, string random, string path, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings["SecretKey"]));
        var signature = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(random + path + body)));
        var plain = $"apiKey:{settings["ApiKey"]}&randomKey:{random}&signature:{signature}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
    }
}
