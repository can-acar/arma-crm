# e-Fatura ve e-Arşiv

Arma faturayı entegratörden bağımsız bir altyapıyla keser. Yerleşik olarak "GİB portalı (dosya)" gelir: Arma
UBL-TR 1.2 belgesini hazırlar, kullanıcı onu GİB e-Arşiv portalına yükler. Özel entegratörler (Paraşüt, Uyumsoft,
Logo, İzibiz…) eklentiyle eklenir ve Arma'nın koduna dokunulmaz.

## Akış

1. Teklif kabul edilir ya da aidat alınır. Satış ve Üyelik modülleri `InvoiceRequested` mesajını yayımlar.
2. Fatura modülü ayarlara bakar (Teklif kabul edildiğinde / Aidat alındığında), alıcıyı kişi kaydından kopyalar ve
   taslak fatura açar. Aynı kaynak için ikinci fatura açılmaz (benzersiz indeks).
3. Görevli taslağı kontrol eder, gerekirse alıcıyı düzeltir ve **Faturayı gönder**'e basar.
4. Belge `EInvoiceDocumentBuilder` ile kurulur ve denetlenir. Kurala uymuyorsa numara harcanmadan hata gösterilir.
5. Sıradaki numara verilip kaydedilir, belge gönderim zincirinden geçip entegratöre gider.
6. Entegratör kabul ederse fatura **Kesildi** olur ve XML saklanır. Reddederse numarasıyla taslak kalır; yeniden
   denemede aynı numara kullanılır.

## Desenler

| Parça | Desen | Nerede |
| --- | --- | --- |
| `IEInvoiceProvider` | Strateji: her entegratör belgeyi kendi yöntemiyle iletir | `Arma.SharedKernel.Invoicing` |
| `IEInvoiceProviderFactory` | Fabrika: anahtardan entegratörü ve ayarlarını üretir; canlıda geliştirmeye özel olanları gizler | `Arma.Modules.Invoicing.Pipeline` |
| `EInvoiceDocumentBuilder` | Kurucu: belgeyi adım adım kurar, eksikleri tek mesajda bildirir, tutarları ve XML'i üretir | `Arma.SharedKernel.Invoicing` |
| `AddArmaEInvoicing()` → `EInvoicingBuilder` | Kurucu: entegratörleri ve işleyicileri DI'a kaydeder, derlemeleri tarar | `Arma.Modules.Invoicing.Pipeline` |
| `IEInvoiceHandler` | Sorumluluk zinciri: gönderim sırayla işleyicilerden geçer, son halka entegratördür | `Arma.SharedKernel.Invoicing` |
| `IEInvoiceGateway` | Fatura modülünün kullandığı tek giriş noktası | `Arma.SharedKernel.Invoicing` |

Zincir:

```
hata yakalama (-1000) → entegratör çözümleme ve ayar kontrolü (0) → belge doğrulama (100) → senaryo kontrolü (200) → [eklenti işleyicileri] → entegratör
```

## Yeni entegratör eklemek

Eklentiye `IEInvoiceProvider` uygulayan bir sınıf koymak yeterlidir (örnek: `samples/Arma.Plugins.Sample/SampleEInvoiceProvider.cs`):

```csharp
public sealed class MyIntegrator(IHttpClientFactory http) : IEInvoiceProvider
{
    public EInvoiceProviderDescriptor Descriptor { get; } = new("benim-entegrator", "Benim Entegratör",
        EInvoiceProfiles.EArsiv | EInvoiceProfiles.EFatura,
        [new("Username", "Kullanıcı adı", IsSecret: false), new("Password", "Parola")]);

    public async Task<EInvoiceSendResult> SendAsync(EInvoiceDocument document, IEInvoiceProviderSettings settings, CancellationToken ct)
    {
        // document.Xml: UBL-TR 1.2, imzasız. document.Number, Uuid, Buyer, Lines, Payable: belge alanları.
        // Entegratörün API'sine gönderin, dönen kimliği referans olarak verin.
        return EInvoiceSendResult.Sent(reference: "...");
    }
}
```

- Ayarlar veritabanına yazılmaz: `EInvoicing:Providers:<anahtar>:<alan>`, kurulumda
  `EInvoicing__Providers__benim-entegrator__Password` ortam değişkeni.
- Anahtar küçük harf, rakam ve tireden oluşur; en fazla 40 karakter. Aynı anahtarlı iki entegratör açılışta hata verir.
- `DevelopmentOnly = true` olan entegratör canlıda listelenmez. `Transmits = false`, faturanın GİB'e elle
  yükleneceğini belirtir; Arma bu faturaları "portala yüklenmeyi bekleyen" olarak listeler.
- Sınıf durumsuz olmalıdır; tek örnek bütün gönderimlerde kullanılır. HTTP için `IHttpClientFactory` ve
  `EInvoicingBuilder.HttpClientName` istemcisi kullanılabilir.
- Zincire kural eklemek için `IEInvoiceHandler` uygulayıp eklenti modülünün `AddServices` içinde
  `services.AddScoped<IEInvoiceHandler, MyHandler>()` ile kaydedin.

## Bilinen sınırlar

- XML imzasızdır; mali mühür veya e-imza entegratörde ya da portalda eklenir.
- Alıcının e-Fatura mükellefi olup olmadığı GİB listesinden sorgulanmaz; görevli taslakta senaryoyu seçer.
- Kesilmiş faturanın iptali Arma'da işaretlenir; GİB'e iletilmiş faturayı entegratörde veya portalda da iptal edin.
- Aidat tutarı KDV dahil kabul edilir; KDV içinden ayrılırken kuruş yuvarlaması olabilir.
