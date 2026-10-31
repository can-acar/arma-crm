using Microsoft.AspNetCore.Http;

namespace Arma.SharedKernel.Payments;

/// <summary>Bir ödeme sağlayıcısının yapabildikleri.</summary>
[Flags]
public enum PaymentCapabilities
{
    None = 0,

    /// <summary>Tek seferlik ödeme: kullanıcı sağlayıcının güvenli sayfasında öder.</summary>
    OneTime = 1,

    /// <summary>İlk ödemede kart sağlayıcıda saklanır; sonraki tahsilatlar kullanıcı olmadan yapılır (düzenli bağış).</summary>
    StoredCard = 2,
}

/// <summary>
/// Sağlayıcının ihtiyaç duyduğu bir ayar. Değerler veritabanında tutulmaz; ortam değişkeni veya ayar dosyasından
/// <c>Payments:Providers:{anahtar}:{alan}</c> yolundan okunur (ör. <c>Payments__Providers__iyzico__ApiKey</c>).
/// </summary>
public sealed record PaymentSettingField(string Key, string Label, bool IsSecret = true, bool IsRequired = true, string? DefaultValue = null, string? Help = null);

/// <summary>Sağlayıcının kendini tanıttığı bilgiler; yönetim ekranındaki listede ve bağış formundaki seçenekte gösterilir.</summary>
public sealed record PaymentProviderDescriptor(string Key, string DisplayName, PaymentCapabilities Capabilities, IReadOnlyList<PaymentSettingField> Settings)
{
    public string? Description { get; init; }

    public string? Website { get; init; }

    /// <summary>Listede sıralama; küçük olan önce.</summary>
    public int Order { get; init; } = 100;

    /// <summary>Yalnızca geliştirme ve testte kullanılabilir; canlı ortamda listelenmez.</summary>
    public bool DevelopmentOnly { get; init; }

    public bool Supports(PaymentCapabilities capability) => (Capabilities & capability) == capability;
}

/// <summary>Bir sağlayıcının yapılandırılmış ayarları.</summary>
public interface IPaymentProviderSettings
{
    string ProviderKey { get; }

    /// <summary>Alanın değeri; girilmemişse tanımdaki varsayılan, o da yoksa boş metin.</summary>
    string this[string field] { get; }

    /// <summary>Zorunlu olup girilmemiş alanlar.</summary>
    IReadOnlyList<PaymentSettingField> Missing { get; }

    bool IsConfigured => Missing.Count == 0;

    bool GetFlag(string field) => bool.TryParse(this[field], out var value) && value;
}

/// <summary>
/// Ödeme sağlayıcısı (strateji). Yeni bir sağlayıcı, bu arayüzü uygulayan bir sınıfı içeren eklentiyle eklenir:
/// sınıf otomatik bulunur, kaydedilir, yönetim ekranında listelenir ve bağış formunda seçenek olur. Başka bir
/// yerde kod değişikliği gerekmez. Sınıf durumsuz olmalıdır; tek örnek bütün istekler için kullanılır.
/// </summary>
public interface IPaymentProvider
{
    PaymentProviderDescriptor Descriptor { get; }

    /// <summary>Ödeme sayfasını açar: kullanıcı yönlendirilir ya da sayfa çerçeve içinde gösterilir.</summary>
    Task<PaymentStart> StartAsync(PaymentRequest request, IPaymentProviderSettings settings, CancellationToken cancellationToken);

    /// <summary>Geri çağrıyı doğrular; imza veya sorgu geçersizse null döner.</summary>
    Task<PaymentNotification?> VerifyCallbackAsync(IFormCollection form, IPaymentProviderSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Geri çağrıya verilecek yanıt. Tarayıcıdan gelen geri çağrıda kullanıcı sonuç sayfasına yönlendirilir;
    /// sunucudan gelen bildirimde sağlayıcının beklediği yanıt (ör. PayTR için "OK") döner.
    /// </summary>
    IResult Acknowledge(PaymentNotification? notification, string successUrl, string failUrl);

    /// <summary>Çerçeve içinde gösterilmesine izin verilen ödeme sayfası adresleri.</summary>
    bool AllowsFrame(string url) => false;
}

/// <summary>Kayıtlı karttan, kullanıcı olmadan tahsilat yapabilen sağlayıcı (düzenli bağış için).</summary>
public interface IStoredCardPaymentProvider : IPaymentProvider
{
    Task<PaymentResult> ChargeAsync(StoredCardCharge charge, IPaymentProviderSettings settings, CancellationToken cancellationToken);
}
