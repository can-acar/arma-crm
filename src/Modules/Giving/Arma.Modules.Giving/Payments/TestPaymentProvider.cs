using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Arma.Modules.Giving.Payments;

/// <summary>
/// Geliştirme ve test için sahte ödeme sayfası. Canlı ortamda kaydedilmez. Geri çağrı, sunucunun
/// veri koruma anahtarıyla imzalanır; böylece testler gerçek sağlayıcıyla aynı doğrulama yolundan geçer.
/// </summary>
internal sealed class TestPaymentProvider(IDataProtectionProvider protection) : IPaymentProvider
{
    private readonly IDataProtector protector = protection.CreateProtector("Arma.Giving.TestPayments");

    public string Name => "test";

    public Task<PaymentStart> StartAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        var query = $"order={request.OrderId}&amount={request.Amount.ToString(CultureInfo.InvariantCulture)}&callback={Uri.EscapeDataString(request.CallbackUrl)}";
        return Task.FromResult(new PaymentStart($"/bagis/test-odeme?{query}&sig={Uri.EscapeDataString(Sign(request.OrderId, request.Amount.ToString(CultureInfo.InvariantCulture), "success"))}&failsig={Uri.EscapeDataString(Sign(request.OrderId, request.Amount.ToString(CultureInfo.InvariantCulture), "failed"))}", null, null));
    }

    public Task<PaymentNotification?> VerifyCallbackAsync(IFormCollection form, CancellationToken cancellationToken)
    {
        string order = form["order"].ToString(), amount = form["amount"].ToString(), status = form["status"].ToString();
        if (!Verify(order, amount, status, form["sig"].ToString()))
        {
            return Task.FromResult<PaymentNotification?>(null);
        }

        return Task.FromResult<PaymentNotification?>(new PaymentNotification(order, status == "success",
            decimal.Parse(amount, CultureInfo.InvariantCulture), "TEST-" + order, status == "success" ? null : "Test ödemesi reddedildi"));
    }

    public IResult Acknowledge(PaymentNotification? notification, string successUrl, string failUrl) =>
        Results.Redirect(notification?.Success == true ? successUrl : failUrl);

    internal string Sign(string order, string amount, string status) =>
        protector.Protect($"{order}|{amount}|{status}");

    private bool Verify(string order, string amount, string status, string signature)
    {
        try
        {
            var expected = Encoding.UTF8.GetBytes($"{order}|{amount}|{status}");
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(protector.Unprotect(signature)), expected);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
