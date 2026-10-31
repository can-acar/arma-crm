namespace Arma.SharedKernel.Invoicing;

/// <summary>Bir entegratör ve bu kurulumdaki hazır olma durumu.</summary>
public sealed record EInvoiceProviderInfo(EInvoiceProviderDescriptor Descriptor, IReadOnlyList<EInvoiceSettingField> MissingSettings)
{
    public string Key => Descriptor.Key;

    public bool IsConfigured => MissingSettings.Count == 0;
}

/// <summary>Fatura gönderiminin tek giriş noktası (cephe). Her gönderim işleyici zincirinden geçip entegratöre ulaşır.</summary>
public interface IEInvoiceGateway
{
    /// <summary>Bu ortamda kullanılabilen entegratörler, sıralı.</summary>
    IReadOnlyList<EInvoiceProviderInfo> Providers { get; }

    EInvoiceProviderInfo? Find(string providerKey);

    Task<EInvoiceSendResult> SendAsync(string providerKey, EInvoiceDocument document, CancellationToken cancellationToken = default);
}

/// <summary>Zincirde dolaşan gönderim bilgisi. Çözümleme işleyicisi entegratörü ve ayarlarını doldurur.</summary>
public sealed class EInvoiceSendContext(string providerKey, EInvoiceDocument document)
{
    public string ProviderKey { get; } = providerKey;

    public EInvoiceDocument Document { get; } = document;

    public IEInvoiceProvider? Provider { get; set; }

    public IEInvoiceProviderSettings? Settings { get; set; }

    /// <summary>İşleyicilerin birbirine bilgi bırakabileceği alan.</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
}

/// <summary>
/// Gönderim zincirinin bir halkası (sorumluluk zinciri). İşleyici <paramref name="next"/>'i çağırarak devam eder ya da
/// çağırmadan kendi sonucunu döndürerek zinciri keser. Eklentiler kendi işleyicilerini (onay, kayıt, kural) ekleyebilir.
/// Yerleşik sıralar: koruma -1000, çözümleme 0, doğrulama 100, senaryo 200.
/// </summary>
public interface IEInvoiceHandler
{
    int Order { get; }

    Task<EInvoiceSendResult> HandleAsync(EInvoiceSendContext context, Func<Task<EInvoiceSendResult>> next, CancellationToken cancellationToken);
}
