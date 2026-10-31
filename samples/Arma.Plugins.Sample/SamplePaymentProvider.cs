using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Arma.SharedKernel.Payments;
using Microsoft.AspNetCore.Http;

namespace Arma.Plugins.Sample;

/// <summary>
/// Örnek ödeme sağlayıcısı. Eklentide bu sınıfın bulunması yeterlidir: Arma onu kendiliğinden bulur, "Makbuz ve ödeme"
/// ekranında listeler ve ayarı girilince bağış formunda seçenek yapar. Gerçek bir sağlayıcı burada kendi ödeme
/// sayfasına yönlendirir; örnek, eklentinin kendi basit sayfasını açar. Yalnızca geliştirmede görünür.
/// </summary>
public sealed class SamplePaymentProvider : IPaymentProvider
{
    public const string Key = "ornek";

    public PaymentProviderDescriptor Descriptor { get; } = new(Key, "Örnek sağlayıcı (eklenti)", PaymentCapabilities.OneTime,
        [new("Secret", "İmza anahtarı", Help: "Payments__Providers__ornek__Secret")])
    {
        Description = "Eklentiyle eklenen ödeme sağlayıcısı örneği; gerçek ödeme almaz.",
        Order = 2000,
        DevelopmentOnly = true,
    };

    public Task<PaymentStart> StartAsync(PaymentRequest request, IPaymentProviderSettings settings, CancellationToken cancellationToken)
    {
        var amount = request.Amount.ToString(CultureInfo.InvariantCulture);
        var sig = Sign(settings, request.OrderId, amount, request.CallbackUrl);
        return Task.FromResult(PaymentStart.Redirect(
            $"/api/v1/plugins/ornek/odeme?order={request.OrderId}&amount={amount}&cb={Uri.EscapeDataString(request.CallbackUrl)}&sig={Uri.EscapeDataString(sig)}"));
    }

    public Task<PaymentNotification?> VerifyCallbackAsync(IFormCollection form, IPaymentProviderSettings settings, CancellationToken cancellationToken)
    {
        string order = form["order"].ToString(), amount = form["amount"].ToString(), cb = form["cb"].ToString();
        var valid = decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var paid)
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(settings, order, amount, cb)), Encoding.ASCII.GetBytes(form["sig"].ToString()));
        return Task.FromResult(valid ? new PaymentNotification(order, true, paid, "ORNEK-" + order, null) : null);
    }

    public IResult Acknowledge(PaymentNotification? notification, string successUrl, string failUrl) =>
        Results.Redirect(notification?.Success == true ? successUrl : failUrl);

    /// <summary>Eklentinin "ödeme sayfası": imzalı bilgileri geri çağrı adresine gönderen tek düğmeli form.</summary>
    internal static IResult Page(string order, string amount, string cb, string sig) =>
        Results.Content($"""
            <!doctype html><html lang="tr"><meta charset="utf-8"><title>Örnek ödeme</title>
            <body style="font-family:sans-serif;max-width:28rem;margin:3rem auto">
            <h1>Örnek ödeme sayfası</h1><p>Tutar: {WebUtility.HtmlEncode(amount)} TL</p>
            <form method="post" action="{WebUtility.HtmlEncode(cb)}">
            <input type="hidden" name="order" value="{WebUtility.HtmlEncode(order)}">
            <input type="hidden" name="amount" value="{WebUtility.HtmlEncode(amount)}">
            <input type="hidden" name="cb" value="{WebUtility.HtmlEncode(cb)}">
            <input type="hidden" name="sig" value="{WebUtility.HtmlEncode(sig)}">
            <button type="submit">Öde</button></form></body></html>
            """, "text/html; charset=utf-8");

    internal static string Sign(IPaymentProviderSettings settings, string order, string amount, string callback)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings["Secret"]));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{order}|{amount}|{callback}")));
    }
}
