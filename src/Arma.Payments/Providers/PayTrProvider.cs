using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arma.SharedKernel.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Arma.Payments.Providers;

/// <summary>PayTR iFrame API. Sonuç, PayTR sunucusundan "bildirim URL" adresine POST ile gelir.</summary>
public sealed class PayTrProvider(IHttpClientFactory httpClients, ILogger<PayTrProvider> logger) : IPaymentProvider
{
    private const string TokenUrl = "https://www.paytr.com/odeme/api/get-token";
    private const string FramePrefix = "https://www.paytr.com/odeme/guvenli/";

    public PaymentProviderDescriptor Descriptor { get; } = new("paytr", "PayTR", PaymentCapabilities.OneTime,
    [
        new("MerchantId", "Mağaza numarası", IsSecret: false),
        new("MerchantKey", "Mağaza parolası (merchant_key)"),
        new("MerchantSalt", "Mağaza gizli anahtarı (merchant_salt)"),
        new("TestMode", "Test modu", IsSecret: false, IsRequired: false, DefaultValue: "false",
            Help: "true iken PayTR test kartlarıyla işlem yapılır, para çekilmez."),
    ])
    {
        Description = "Kartla ödeme PayTR'nin güvenli sayfasında, bağış formunun içinde alınır. Bildirim adresi PayTR panelinde tanımlanmalıdır.",
        Website = "https://www.paytr.com",
        Order = 20,
    };

    public async Task<PaymentStart> StartAsync(PaymentRequest request, IPaymentProviderSettings settings, CancellationToken cancellationToken)
    {
        var amount = ToKurus(request.Amount);
        var basket = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object[][]
        {
            [request.Description, request.Amount.ToString("0.00", CultureInfo.InvariantCulture), 1],
        })));
        const string noInstallment = "1";
        const string maxInstallment = "0";
        const string currency = "TL";
        var testMode = settings.GetFlag("TestMode") ? "1" : "0";

        var hashInput = settings["MerchantId"] + request.Payer.Ip + request.OrderId + request.Payer.Email + amount + basket
            + noInstallment + maxInstallment + currency + testMode;
        var token = Sign(settings, hashInput + settings["MerchantSalt"]);

        var form = new Dictionary<string, string>
        {
            ["merchant_id"] = settings["MerchantId"],
            ["user_ip"] = request.Payer.Ip,
            ["merchant_oid"] = request.OrderId,
            ["email"] = request.Payer.Email,
            ["payment_amount"] = amount,
            ["paytr_token"] = token,
            ["user_basket"] = basket,
            ["debug_on"] = "0",
            ["no_installment"] = noInstallment,
            ["max_installment"] = maxInstallment,
            ["user_name"] = request.Payer.FullName,
            ["user_address"] = "-",
            ["user_phone"] = string.IsNullOrWhiteSpace(request.Payer.Phone) ? "-" : request.Payer.Phone,
            ["merchant_ok_url"] = request.SuccessUrl,
            ["merchant_fail_url"] = request.FailUrl,
            ["timeout_limit"] = "30",
            ["currency"] = currency,
            ["test_mode"] = testMode,
            ["lang"] = "tr",
        };

        try
        {
            using var response = await httpClients.CreateClient(PaymentsBuilder.HttpClientName).PostAsync(TokenUrl, new FormUrlEncodedContent(form), cancellationToken);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (json.RootElement.GetProperty("status").GetString() == "success")
            {
                return PaymentStart.Frame(FramePrefix + json.RootElement.GetProperty("token").GetString());
            }

            var reason = json.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
            logger.LogWarning("PayTR ödeme başlatılamadı: {Reason}", reason);
            return PaymentStart.Failed("Ödeme sayfası açılamadı. Lütfen daha sonra tekrar deneyin.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "PayTR'ye bağlanılamadı");
            return PaymentStart.Failed("Ödeme sistemine bağlanılamadı. Lütfen daha sonra tekrar deneyin.");
        }
    }

    public Task<PaymentNotification?> VerifyCallbackAsync(IFormCollection form, IPaymentProviderSettings settings, CancellationToken cancellationToken)
    {
        string oid = form["merchant_oid"].ToString(), status = form["status"].ToString(), total = form["total_amount"].ToString();
        var expected = Sign(settings, oid + settings["MerchantSalt"] + status + total);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(form["hash"].ToString())))
        {
            logger.LogWarning("PayTR bildiriminin imzası geçersiz ({OrderId})", oid);
            return Task.FromResult<PaymentNotification?>(null);
        }

        // total_amount kuruş cinsindendir; taksitli ödemede vade farkı dahil olabilir.
        var paid = decimal.Parse(form["payment_amount"].ToString() is { Length: > 0 } p ? p : total, CultureInfo.InvariantCulture) / 100m;
        var failure = status == "success" ? null : $"{form["failed_reason_code"]} {form["failed_reason_msg"]}".Trim();
        return Task.FromResult<PaymentNotification?>(new PaymentNotification(oid, status == "success", paid, oid, failure));
    }

    public IResult Acknowledge(PaymentNotification? notification, string successUrl, string failUrl) =>
        notification is null ? Results.BadRequest("PAYTR notification failed: bad hash") : Results.Text("OK");

    public bool AllowsFrame(string url) => url.StartsWith(FramePrefix, StringComparison.Ordinal);

    private static string ToKurus(decimal amount) => ((long)decimal.Round(amount * 100)).ToString(CultureInfo.InvariantCulture);

    internal static string Sign(IPaymentProviderSettings settings, string value)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings["MerchantKey"]));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}
