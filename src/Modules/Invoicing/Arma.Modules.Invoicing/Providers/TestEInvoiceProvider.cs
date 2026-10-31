using Arma.SharedKernel.Invoicing;

namespace Arma.Modules.Invoicing.Providers;

/// <summary>Geliştirme ve test için: faturayı GİB'e iletilmiş sayar. Canlı ortamda listelenmez.</summary>
public sealed class TestEInvoiceProvider : IEInvoiceProvider
{
    public const string Key = "test";

    public EInvoiceProviderDescriptor Descriptor { get; } = new(Key, "Test entegratörü", EInvoiceProfiles.EArsiv | EInvoiceProfiles.EFatura, [])
    {
        Description = "Faturayı göndermeden iletilmiş sayar; yalnızca geliştirmede görünür.",
        Order = 1000,
        DevelopmentOnly = true,
    };

    /// <summary>Testler hata durumunu denemek için açar.</summary>
    public bool Reject { get; set; }

    /// <summary>Son gönderilen belge.</summary>
    public EInvoiceDocument? LastDocument { get; private set; }

    public Task<EInvoiceSendResult> SendAsync(EInvoiceDocument document, IEInvoiceProviderSettings settings, CancellationToken cancellationToken)
    {
        LastDocument = document;
        return Task.FromResult(Reject ? EInvoiceSendResult.Failed("Test entegratörü faturayı reddetti.") : EInvoiceSendResult.Sent("TEST-" + document.Number));
    }
}
