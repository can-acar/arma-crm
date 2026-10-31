# Ödeme altyapısı

Arma'da çevrim içi bağış, sağlayıcıdan bağımsız bir ödeme altyapısıyla alınır. iyzico ve PayTR yerleşik gelir;
başka bir sağlayıcı (banka sanal POS'u, Param, Stripe…) eklentiyle eklenir ve Arma'nın koduna dokunulmaz.

## Nasıl çalışır

| Parça | Desen | Nerede |
| --- | --- | --- |
| `IPaymentProvider`, `IStoredCardPaymentProvider` | Strateji: her sağlayıcı ödemeyi kendi yöntemiyle başlatır ve doğrular | `Arma.SharedKernel.Payments` |
| `IPaymentProviderFactory` | Fabrika: anahtardan sağlayıcıyı ve ayarlarını üretir, canlıda yalnızca geliştirmeye özel sağlayıcıları gizler | `Arma.Payments` |
| `PaymentRequestBuilder` | Kurucu: ödeme isteğini adım adım kurar, eksikleri tek seferde bildirir | `Arma.SharedKernel.Payments` |
| `AddArmaPayments()` → `PaymentsBuilder` | Kurucu: sağlayıcıları ve işleyicileri DI'a kaydeder, derlemeleri tarar | `Arma.Payments` |
| `IPaymentHandler<TContext, TResult>` | Sorumluluk zinciri: her işlem sırayla işleyicilerden geçer, son halka sağlayıcıdır | `Arma.SharedKernel.Payments` |
| `IPaymentGateway` | Modüllerin kullandığı tek giriş noktası | `Arma.SharedKernel.Payments` |

Her işlemin (ödeme başlatma, geri çağrı doğrulama, kayıtlı karttan tahsilat) zinciri şöyledir:

```
hata yakalama (-1000) → sağlayıcı çözümleme ve ayar kontrolü (0) → doğrulama (100) → yetenek kontrolü (200) → [eklenti işleyicileri] → sağlayıcı
```

Bir işleyici isteği kendisi sonuçlandırabilir (ör. "tutar sınırı aşıldı") ya da `next()` ile sıradakine bırakır.

Uygulama açılırken bütün modül ve eklenti derlemeleri taranır; `IPaymentProvider` uygulayan her somut sınıf
kaydedilir. Sağlayıcı "Makbuz ve ödeme" ekranında ayarlarının tam olup olmadığıyla listelenir. İşaretli ve
ayarları tam olan sağlayıcılar bağış formunda seçenek olur; birden fazlaysa bağışçı seçer.

## Ayarlar

Sağlayıcı ayarları veritabanına yazılmaz. `Payments:Providers:<anahtar>:<alan>` yolundan okunur; kurulumda bu,
`.env` dosyasındaki `Payments__Providers__<anahtar>__<alan>` ortam değişkenidir:

```
Payments__Providers__iyzico__ApiKey=...
Payments__Providers__iyzico__SecretKey=...
Payments__Providers__iyzico__BaseUrl=https://api.iyzipay.com
Payments__Providers__paytr__MerchantId=...
Payments__Providers__paytr__MerchantKey=...
Payments__Providers__paytr__MerchantSalt=...
```

Geri çağrı adresi her sağlayıcı için `https://<alan-adı>/api/v1/payments/<anahtar>/callback` olur. PayTR'de bu
adres mağaza panelinde "Bildirim URL" olarak girilmelidir.

## Yeni sağlayıcı ekleme

1. Bir eklenti projesi açın (bkz. [eklentiler.md](eklentiler.md)); yalnızca `Arma.SharedKernel`'e başvurması yeter.
2. `IPaymentProvider` (düzenli bağış da destekleyecekse `IStoredCardPaymentProvider`) uygulayan bir sınıf yazın:

```csharp
public sealed class OrnekPosProvider(IHttpClientFactory http) : IPaymentProvider
{
    public PaymentProviderDescriptor Descriptor { get; } = new("ornekpos", "Örnek POS", PaymentCapabilities.OneTime,
    [
        new("MerchantId", "Üye işyeri numarası", IsSecret: false),
        new("Secret", "Gizli anahtar"),
    ])
    {
        Description = "Kartla ödeme Örnek Bankası'nın güvenli sayfasında alınır.",
    };

    public Task<PaymentStart> StartAsync(PaymentRequest request, IPaymentProviderSettings settings, CancellationToken ct)
    {
        // Sağlayıcının ödeme sayfasını açın; kullanıcıyı yönlendirin ya da çerçeve adresini döndürün.
        return Task.FromResult(PaymentStart.Redirect("https://pos.ornek.com/odeme?..."));
    }

    public Task<PaymentNotification?> VerifyCallbackAsync(IFormCollection form, IPaymentProviderSettings settings, CancellationToken ct)
    {
        // İmzayı settings["Secret"] ile doğrulayın ya da sonucu sağlayıcıdan sorgulayın. Geçersizse null döndürün.
    }

    public IResult Acknowledge(PaymentNotification? notification, string successUrl, string failUrl) =>
        Results.Redirect(notification?.Success == true ? successUrl : failUrl);
}
```

3. Eklentiyi `plugins/<eklenti-kimliği>/` klasörüne koyun, ayarları `.env` dosyasına ekleyin ve uygulamayı yeniden
   başlatın. Sağlayıcı "Makbuz ve ödeme" ekranında görünür; işaretleyip kaydedince bağış formunda seçenek olur.

Kurallar:

- Anahtar küçük harf, rakam ve tireden oluşur ve kurulumda tektir; aynı anahtarlı iki sağlayıcı uygulamayı açtırmaz.
- Sınıf durumsuz olmalıdır; tek örnek bütün isteklerde kullanılır. HTTP için `IHttpClientFactory` kullanın.
- Geri çağrıdaki verilere güvenmeyin: imzayı doğrulayın ya da sonucu sağlayıcıdan sorgulayın. Tutar Arma'da ayrıca
  karşılaştırılır; eşleşmeyen bildirim bağışı tamamlamaz.
- Yalnızca geliştirmede kullanılacak sağlayıcıda `DevelopmentOnly = true` verin; canlı ortamda listelenmez.
- Bir sınıfın taranmasını istemiyorsanız `[ExcludeFromDiscovery]` ile işaretleyin.

## Zincire işleyici ekleme

Eklenti, ödeme zincirine kendi kontrolünü ekleyebilir. Örneğin tek seferde alınabilecek tutarı sınırlamak için:

```csharp
public sealed class TutarSiniri : IPaymentHandler<PaymentStartContext, PaymentStart>
{
    public int Order => 150;

    public Task<PaymentStart> HandleAsync(PaymentStartContext context, Func<Task<PaymentStart>> next, CancellationToken ct) =>
        context.Request.Amount > 50_000m ? Task.FromResult(PaymentStart.Failed("Bu tutar için kurumla iletişime geçin.")) : next();
}

// Eklentinin IModule.AddServices yönteminde:
services.AddScoped<IPaymentHandler<PaymentStartContext, PaymentStart>, TutarSiniri>();
```

## Düzenli bağış

Bağışçı formda "Her ay tekrarla"yı seçerse ödeme `StoreCard()` ile başlatılır. Sağlayıcı geri çağrıda kayıtlı kartın
jetonunu (`PaymentNotification.Card`) döndürür; Arma jetonu veri koruma anahtarıyla şifreleyip saklar. Saatte bir
çalışan iş, günü gelen düzenli bağışları `IStoredCardPaymentProvider.ChargeAsync` ile tahsil eder ve her tahsilatı
ayrı bağış olarak kaydeder.

- iyzico: üye işyerinde kart saklama ve kayıtlı karttan 3D'siz ödeme yetkisi açık olmalıdır. Bağışçı ödeme
  sayfasında kartını kaydetmezse bağış tek seferlik kalır ve düzenli bağış başlamaz.
- PayTR: iFrame API kart saklamayı desteklemediği için PayTR şu an yalnızca tek seferlik bağışta sunulur.
