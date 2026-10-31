using Microsoft.AspNetCore.Http;

namespace Arma.SharedKernel.Payments;

/// <summary>Bir sağlayıcının bu kurulumdaki durumu.</summary>
public sealed record PaymentProviderInfo(PaymentProviderDescriptor Descriptor, IReadOnlyList<PaymentSettingField> MissingSettings)
{
    public string Key => Descriptor.Key;

    public bool IsConfigured => MissingSettings.Count == 0;
}

/// <summary>
/// Modüllerin ödeme için kullandığı tek giriş noktası. Her işlem bir işleyici zincirinden geçer
/// (<see cref="IPaymentHandler{TContext,TResult}"/>); zincirin sonunda seçilen sağlayıcı çalışır.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Bu kurulumda bulunan bütün sağlayıcılar (yapılandırılmış olsun olmasın), sıralı.</summary>
    IReadOnlyList<PaymentProviderInfo> Providers { get; }

    PaymentProviderInfo? Find(string providerKey);

    Task<PaymentStart> StartAsync(PaymentRequest request, CancellationToken cancellationToken = default);

    Task<PaymentNotification?> VerifyCallbackAsync(string providerKey, IFormCollection form, CancellationToken cancellationToken = default);

    IResult Acknowledge(string providerKey, PaymentNotification? notification, string successUrl, string failUrl);

    Task<PaymentResult> ChargeStoredCardAsync(StoredCardCharge charge, CancellationToken cancellationToken = default);

    /// <summary>Adres, sağlayıcının çerçeve içinde gösterilebilen ödeme sayfası mı.</summary>
    bool AllowsFrame(string providerKey, string url);
}

/// <summary>Zincirdeki işleyicilerin paylaştığı bağlam. Sağlayıcı, çözümleme işleyicisi tarafından doldurulur.</summary>
public abstract class PaymentContext(string providerKey)
{
    public string ProviderKey { get; } = providerKey;

    public IPaymentProvider? Provider { get; set; }

    public IPaymentProviderSettings? Settings { get; set; }

    /// <summary>İşleyicilerin birbirine bilgi bırakması için.</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
}

public sealed class PaymentStartContext(PaymentRequest request) : PaymentContext(request.ProviderKey)
{
    public PaymentRequest Request { get; set; } = request;
}

public sealed class PaymentCallbackContext(string providerKey, IFormCollection form) : PaymentContext(providerKey)
{
    public IFormCollection Form { get; } = form;
}

public sealed class StoredCardChargeContext(StoredCardCharge charge) : PaymentContext(charge.ProviderKey)
{
    public StoredCardCharge Charge { get; set; } = charge;
}

/// <summary>
/// Ödeme zincirinin bir halkası (sorumluluk zinciri). İşleyici isteği kendisi sonuçlandırabilir ya da
/// <paramref name="next"/> ile sıradakine bırakır. Eklentiler kendi işleyicilerini (dolandırıcılık kontrolü,
/// tutar sınırı, kayıt) DI'a <c>IPaymentHandler&lt;PaymentStartContext, PaymentStart&gt;</c> olarak ekleyerek
/// zincire katar; <see cref="Order"/> sırayı belirler.
/// </summary>
public interface IPaymentHandler<in TContext, TResult> where TContext : PaymentContext
{
    /// <summary>Küçük olan önce çalışır. Yerleşik işleyiciler: -1000 hata yakalama, 0 sağlayıcı çözümleme, 100 doğrulama, 200 yetenek kontrolü.</summary>
    int Order { get; }

    Task<TResult> HandleAsync(TContext context, Func<Task<TResult>> next, CancellationToken cancellationToken);
}
