using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arma.Modules.Giving.Payments;

/// <summary>PayTR iFrame API. Sonuç, PayTR sunucusundan "bildirim URL" adresine POST ile gelir.</summary>
internal sealed class PayTrProvider(HttpClient http, IOptions<PaymentOptions> options, ILogger<PayTrProvider> logger) : IPaymentProvider
{
    private const string TokenUrl = "https://www.paytr.com/odeme/api/get-token";

    private PayTrOptions Settings => options.Value.PayTR;

    public string Name => "paytr";

    public async Task<PaymentStart> StartAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        var amount = ToKurus(request.Amount);
        var basket = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object[][]
        {
            [request.Description, request.Amount.ToString("0.00", CultureInfo.InvariantCulture), 1],
        })));
        const string noInstallment = "1";
        const string maxInstallment = "0";
        const string currency = "TL";
        var testMode = Settings.TestMode ? "1" : "0";

        var hashInput = Settings.MerchantId + request.Payer.Ip + request.OrderId + request.Payer.Email + amount + basket
            + noInstallment + maxInstallment + currency + testMode;
        var token = Sign(hashInput + Settings.MerchantSalt);

        var form = new Dictionary<string, string>
        {
            ["merchant_id"] = Settings.MerchantId,
            ["user_ip"] = request.Payer.Ip,
            ["merchant_oid"] = request.OrderId,
            ["email"] = request.Payer.Email,
            ["payment_amount"] = amount,
            ["paytr_token"] = token,
            ["user_basket"] = basket,
            ["debug_on"] = "0",
            ["no_installment"] = noInstallment,
            ["max_installment"] = maxInstallment,
            ["user_name"] = $"{request.Payer.FirstName} {request.Payer.LastName}",
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
            using var response = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), cancellationToken);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (json.RootElement.GetProperty("status").GetString() == "success")
            {
                return new PaymentStart(null, "https://www.paytr.com/odeme/guvenli/" + json.RootElement.GetProperty("token").GetString(), null);
            }

            var reason = json.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
            logger.LogWarning("PayTR ödeme başlatılamadı: {Reason}", reason);
            return PaymentStart.Failed("Ödeme sayfası açılamadı. Lütfen daha sonra tekrar deneyin.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException)
        {
            logger.LogError(ex, "PayTR'ye bağlanılamadı");
            return PaymentStart.Failed("Ödeme sistemine bağlanılamadı. Lütfen daha sonra tekrar deneyin.");
        }
    }

    public Task<PaymentNotification?> VerifyCallbackAsync(IFormCollection form, CancellationToken cancellationToken)
    {
        string oid = form["merchant_oid"].ToString(), status = form["status"].ToString(), total = form["total_amount"].ToString();
        var expected = Sign(oid + Settings.MerchantSalt + status + total);
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

    private static string ToKurus(decimal amount) => ((long)decimal.Round(amount * 100)).ToString(CultureInfo.InvariantCulture);

    private string Sign(string value)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Settings.MerchantKey));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}
