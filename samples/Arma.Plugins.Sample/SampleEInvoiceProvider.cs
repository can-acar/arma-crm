using Arma.SharedKernel.Invoicing;

namespace Arma.Plugins.Sample;

/// <summary>
/// Örnek e-Fatura entegratörü. Eklentide bu sınıfın bulunması yeterlidir: Arma onu kendiliğinden bulur ve "Fatura ayarları"
/// ekranında listeler; ayarı girilip seçilince faturalar onunla gönderilir. Gerçek bir entegratör burada
/// <see cref="EInvoiceDocument.Xml"/> belgesini kendi API'sine gönderir. Yalnızca geliştirmede görünür.
/// </summary>
public sealed class SampleEInvoiceProvider : IEInvoiceProvider
{
    public const string Key = "ornek-entegrator";

    public EInvoiceProviderDescriptor Descriptor { get; } = new(Key, "Örnek entegratör (eklenti)", EInvoiceProfiles.EArsiv,
        [new("Token", "API anahtarı", Help: "EInvoicing__Providers__ornek-entegrator__Token")])
    {
        Description = "Eklentiyle eklenen entegratör örneği; faturayı GİB'e göndermez.",
        Order = 2000,
        DevelopmentOnly = true,
    };

    public Task<EInvoiceSendResult> SendAsync(EInvoiceDocument document, IEInvoiceProviderSettings settings, CancellationToken cancellationToken) =>
        Task.FromResult(document.Xml.Contains(document.Number, StringComparison.Ordinal)
            ? EInvoiceSendResult.Sent("ORNEK-" + document.Uuid.ToString("N")[..12].ToUpperInvariant())
            : EInvoiceSendResult.Failed("Belge numarası XML'de yok."));
}
