using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Arma.SharedKernel.Payments;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Arma.Payments.Providers;

/// <summary>
/// Geliştirme ve test için sahte ödeme sayfası (/bagis/test-odeme). Canlı ortamda listelenmez. Geri çağrı sunucunun
/// veri koruma anahtarıyla imzalanır; böylece testler gerçek sağlayıcıyla aynı doğrulama yolundan geçer.
/// </summary>
public sealed class TestPaymentProvider(IDataProtectionProvider protection) : IStoredCardPaymentProvider
{
    private readonly IDataProtector protector = protection.CreateProtector("Arma.Payments.Test");

    public PaymentProviderDescriptor Descriptor { get; } = new("test", "Test ödemesi", PaymentCapabilities.OneTime | PaymentCapabilities.StoredCard, [])
    {
        Description = "Gerçek kart kullanılmaz. Yalnızca geliştirme ve test ortamında görünür.",
        Order = 1000,
        DevelopmentOnly = true,
    };

    /// <summary>Açıkken kayıtlı karttan tahsilatlar reddedilir (testlerde başarısız tahsilatı denemek için).</summary>
    public bool DeclineCharges { get; set; }

    public Task<PaymentStart> StartAsync(PaymentRequest request, IPaymentProviderSettings settings, CancellationToken cancellationToken)
    {
        var amount = request.Amount.ToString(CultureInfo.InvariantCulture);
        var store = request.StoreCard ? "1" : "0";
        var query = $"order={request.OrderId}&amount={amount}&store={store}&callback={Uri.EscapeDataString(request.CallbackUrl)}"
            + $"&sig={Uri.EscapeDataString(Sign(request.OrderId, amount, "success", store))}&failsig={Uri.EscapeDataString(Sign(request.OrderId, amount, "failed", store))}";
        return Task.FromResult(PaymentStart.Redirect($"/bagis/test-odeme?{query}"));
    }

    public Task<PaymentNotification?> VerifyCallbackAsync(IFormCollection form, IPaymentProviderSettings settings, CancellationToken cancellationToken)
    {
        string order = form["order"].ToString(), amount = form["amount"].ToString(), status = form["status"].ToString(), store = form["store"].ToString();
        if (!Verify(order, amount, status, store, form["sig"].ToString()))
        {
            return Task.FromResult<PaymentNotification?>(null);
        }

        var success = status == "success";
        var card = success && store == "1" ? new StoredCard("test-card-" + order, "TEST **** 4242") : null;
        return Task.FromResult<PaymentNotification?>(new PaymentNotification(order, success,
            decimal.Parse(amount, CultureInfo.InvariantCulture), "TEST-" + order, success ? null : "Test ödemesi reddedildi", card));
    }

    public IResult Acknowledge(PaymentNotification? notification, string successUrl, string failUrl) =>
        Results.Redirect(notification?.Success == true ? successUrl : failUrl);

    public Task<PaymentResult> ChargeAsync(StoredCardCharge charge, IPaymentProviderSettings settings, CancellationToken cancellationToken) =>
        Task.FromResult(DeclineCharges || !charge.Card.Token.StartsWith("test-card-", StringComparison.Ordinal)
            ? PaymentResult.Failed("Test kartı reddedildi")
            : PaymentResult.Paid("TEST-" + charge.OrderId));

    public string Sign(string order, string amount, string status, string store = "0") =>
        protector.Protect($"{order}|{amount}|{status}|{store}");

    private bool Verify(string order, string amount, string status, string store, string signature)
    {
        try
        {
            var expected = Encoding.UTF8.GetBytes($"{order}|{amount}|{status}|{(store.Length == 0 ? "0" : store)}");
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(protector.Unprotect(signature)), expected);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
