using Arma.SharedKernel.Invoicing;

namespace Arma.Modules.Invoicing.Providers;

/// <summary>
/// Entegratörü olmayan kurumlar için: fatura UBL-TR dosyası olarak hazırlanır, kullanıcı onu GİB e-Arşiv portalına
/// (earsivportal.efatura.gov.tr) yükleyip orada onaylar, sonra Arma'da "GİB'e iletildi" olarak işaretler. Ayar gerektirmez.
/// </summary>
public sealed class GibPortalFileProvider : IEInvoiceProvider
{
    public const string Key = "gib-portal";

    public EInvoiceProviderDescriptor Descriptor { get; } = new(Key, "GİB portalı (dosya)", EInvoiceProfiles.EArsiv | EInvoiceProfiles.EFatura, [])
    {
        Description = "Fatura XML dosyası hazırlanır; GİB e-Arşiv portalına siz yükler ve onaylarsınız.",
        Website = "https://earsivportal.efatura.gov.tr",
        Order = 10,
        Transmits = false,
    };

    public Task<EInvoiceSendResult> SendAsync(EInvoiceDocument document, IEInvoiceProviderSettings settings, CancellationToken cancellationToken) =>
        Task.FromResult(EInvoiceSendResult.Prepared());
}
