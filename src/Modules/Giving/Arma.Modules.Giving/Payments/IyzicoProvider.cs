using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arma.Modules.Giving.Payments;

/// <summary>
/// iyzico Ödeme Formu (Checkout Form). Kullanıcı iyzico sayfasında öder, tarayıcı token ile geri çağrı adresine
/// döner; sonuç token ile iyzico'dan sorgulanır (geri çağrıdaki verilere güvenilmez).
/// </summary>
internal sealed class IyzicoProvider(HttpClient http, IOptions<PaymentOptions> options, ILogger<IyzicoProvider> logger) : IPaymentProvider
{
    private const string InitializePath = "/payment/iyzipos/checkoutform/initialize/auth/ecom";
    private const string DetailPath = "/payment/iyzipos/checkoutform/auth/ecom/detail";

    // iyzico alıcının kimlik numarasını zorunlu tutar; bağış formunda istenmediğinde belgelerdeki yer tutucu gönderilir.
    private const string PlaceholderIdentityNumber = "11111111111";

    private IyzicoOptions Settings => options.Value.Iyzico;

    public string Name => "iyzico";

    public async Task<PaymentStart> StartAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        var price = request.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        var name = $"{request.Payer.FirstName} {request.Payer.LastName}";
        var body = new JsonObject
        {
            ["locale"] = "tr",
            ["conversationId"] = request.OrderId,
            ["price"] = price,
            ["paidPrice"] = price,
            ["currency"] = "TRY",
            ["basketId"] = request.OrderId,
            ["paymentGroup"] = "PRODUCT",
            ["callbackUrl"] = request.CallbackUrl,
            ["enabledInstallments"] = new JsonArray(1),
            ["buyer"] = new JsonObject
            {
                ["id"] = request.OrderId,
                ["name"] = request.Payer.FirstName,
                ["surname"] = request.Payer.LastName,
                ["identityNumber"] = PlaceholderIdentityNumber,
                ["email"] = request.Payer.Email,
                ["gsmNumber"] = request.Payer.Phone,
                ["registrationAddress"] = "-",
                ["city"] = "-",
                ["country"] = "Turkey",
                ["ip"] = request.Payer.Ip,
            },
            ["billingAddress"] = new JsonObject
            {
                ["contactName"] = name,
                ["city"] = "-",
                ["country"] = "Turkey",
                ["address"] = "-",
            },
            ["basketItems"] = new JsonArray(new JsonObject
            {
                ["id"] = request.OrderId,
                ["name"] = request.Description,
                ["category1"] = "Bağış",
                ["itemType"] = "VIRTUAL",
                ["price"] = price,
            }),
        };

        var response = await SendAsync(InitializePath, body, cancellationToken);
        if (response?["status"]?.GetValue<string>() == "success" && response["paymentPageUrl"]?.GetValue<string>() is { } url)
        {
            return new PaymentStart(url, null, null);
        }

        logger.LogWarning("iyzico ödeme formu açılamadı: {Error}", response?["errorMessage"]?.GetValue<string>());
        return PaymentStart.Failed("Ödeme sayfası açılamadı. Lütfen daha sonra tekrar deneyin.");
    }

    public async Task<PaymentNotification?> VerifyCallbackAsync(IFormCollection form, CancellationToken cancellationToken)
    {
        var token = form["token"].ToString();
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var detail = await SendAsync(DetailPath, new JsonObject { ["locale"] = "tr", ["token"] = token }, cancellationToken);
        if (detail?["status"]?.GetValue<string>() != "success" || detail["basketId"]?.GetValue<string>() is not { } orderId)
        {
            logger.LogWarning("iyzico ödeme sonucu sorgulanamadı: {Error}", detail?["errorMessage"]?.GetValue<string>());
            return null;
        }

        var success = detail["paymentStatus"]?.GetValue<string>() == "SUCCESS";
        var paid = detail["price"] is { } p ? p.GetValue<decimal>() : 0m;
        var paymentId = detail["paymentId"]?.ToString() ?? token;
        return new PaymentNotification(orderId, success, paid, paymentId, success ? null : detail["errorMessage"]?.GetValue<string>());
    }

    public IResult Acknowledge(PaymentNotification? notification, string successUrl, string failUrl) =>
        Results.Redirect(notification?.Success == true ? successUrl : failUrl);

    private async Task<JsonNode?> SendAsync(string path, JsonObject body, CancellationToken cancellationToken)
    {
        var json = body.ToJsonString();
        var random = Guid.NewGuid().ToString("N");
        using var message = new HttpRequestMessage(HttpMethod.Post, Settings.BaseUrl.TrimEnd('/') + path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("IYZWSv2", Authorization(random, path, json));
        message.Headers.Add("x-iyzi-rnd", random);

        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogError(ex, "iyzico'ya bağlanılamadı");
            return null;
        }
    }

    /// <summary>IYZWSv2: base64("apiKey:..&amp;randomKey:..&amp;signature:" + hex(HMACSHA256(secret, random + path + body))).</summary>
    private string Authorization(string random, string path, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Settings.SecretKey));
        var signature = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(random + path + body)));
        var plain = $"apiKey:{Settings.ApiKey}&randomKey:{random}&signature:{signature}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
    }
}
