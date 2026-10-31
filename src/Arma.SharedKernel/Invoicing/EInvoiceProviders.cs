namespace Arma.SharedKernel.Invoicing;

/// <summary>GİB senaryoları: alıcı e-Fatura mükellefi değilse e-Arşiv, mükellefse e-Fatura (temel veya ticari).</summary>
[Flags]
public enum EInvoiceProfiles
{
    None = 0,

    /// <summary>e-Arşiv fatura (EARSIVFATURA): alıcı e-Fatura sisteminde kayıtlı değil, çoğunlukla bireyler.</summary>
    EArsiv = 1,

    /// <summary>e-Fatura (TEMELFATURA veya TICARIFATURA): alıcı e-Fatura mükellefi.</summary>
    EFatura = 2,
}

/// <summary>
/// Entegratörün ihtiyaç duyduğu bir ayar. Değerler veritabanında tutulmaz; ortam değişkeni veya ayar dosyasından
/// <c>EInvoicing:Providers:{anahtar}:{alan}</c> yolundan okunur (ör. <c>EInvoicing__Providers__ornek-entegrator__Token</c>).
/// </summary>
public sealed record EInvoiceSettingField(string Key, string Label, bool IsSecret = true, bool IsRequired = true, string? DefaultValue = null, string? Help = null);

/// <summary>Entegratörün kendini tanıttığı bilgiler; fatura ayarları ekranında listelenir.</summary>
public sealed record EInvoiceProviderDescriptor(string Key, string DisplayName, EInvoiceProfiles Profiles, IReadOnlyList<EInvoiceSettingField> Settings)
{
    public string? Description { get; init; }

    public string? Website { get; init; }

    /// <summary>Listede sıralama; küçük olan önce.</summary>
    public int Order { get; init; } = 100;

    /// <summary>Yalnızca geliştirme ve testte kullanılabilir; canlı ortamda listelenmez.</summary>
    public bool DevelopmentOnly { get; init; }

    /// <summary>
    /// Sağlayıcı faturayı GİB'e kendisi iletir. Yanlışsa fatura dosyası yalnızca hazırlanır; kullanıcı onu GİB portalına
    /// kendisi yükler ve "iletildi" olarak işaretler.
    /// </summary>
    public bool Transmits { get; init; } = true;

    public bool Supports(EInvoiceProfiles profile) => (Profiles & profile) == profile;
}

/// <summary>Bir entegratörün yapılandırılmış ayarları.</summary>
public interface IEInvoiceProviderSettings
{
    string ProviderKey { get; }

    /// <summary>Alanın değeri; girilmemişse tanımdaki varsayılan, o da yoksa boş metin.</summary>
    string this[string field] { get; }

    /// <summary>Zorunlu olup girilmemiş alanlar.</summary>
    IReadOnlyList<EInvoiceSettingField> Missing { get; }

    bool IsConfigured => Missing.Count == 0;

    bool GetFlag(string field) => bool.TryParse(this[field], out var value) && value;
}

/// <summary>Gönderim sonucu. <see cref="Reference"/> entegratörün faturaya verdiği kimliktir (sorgu ve iptal için).</summary>
public sealed record EInvoiceSendResult(bool Success, string? Reference, string? Error)
{
    /// <summary>Fatura GİB'e iletildi (ya da iletilmek üzere entegratöre teslim edildi).</summary>
    public bool Transmitted { get; init; }

    public static EInvoiceSendResult Sent(string? reference) => new(true, reference, null) { Transmitted = true };

    /// <summary>Dosya hazırlandı; kullanıcı portala yükleyecek.</summary>
    public static EInvoiceSendResult Prepared() => new(true, null, null);

    public static EInvoiceSendResult Failed(string error) => new(false, null, error);
}

/// <summary>
/// e-Fatura / e-Arşiv entegratörü (strateji). Yeni bir özel entegratör, bu arayüzü uygulayan bir sınıfı içeren eklentiyle
/// eklenir: sınıf kendiliğinden bulunur, fatura ayarlarında listelenir ve seçilince faturalar onunla gönderilir. Başka bir
/// yerde kod değişikliği gerekmez. Sınıf durumsuz olmalıdır; tek örnek bütün istekler için kullanılır.
/// </summary>
public interface IEInvoiceProvider
{
    EInvoiceProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Faturayı gönderir. <see cref="EInvoiceDocument.Xml"/> UBL-TR 1.2 biçimindeki imzasız faturadır; entegratör
    /// bunu imzalayıp iletir ya da belgenin alanlarından kendi API'sinin beklediği isteği kurar.
    /// </summary>
    Task<EInvoiceSendResult> SendAsync(EInvoiceDocument document, IEInvoiceProviderSettings settings, CancellationToken cancellationToken);
}
