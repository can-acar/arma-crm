# Eklenti geliştirme

Eklenti, Arma'ya kurumun kendi ihtiyacı için sayfa, menü bağlantısı, kişi kartı sekmesi, pano kutucuğu, API ucu, zamanlanmış iş ya da olay dinleyicisi ekleyen bir .NET derlemesidir. Yerleşik modüller de aynı sözleşmeyle (`IModule`) yazılır; eklentinin yerleşik bir modülden tek farkı ayrı derlenip `plugins` klasöründen yüklenmesidir.

> Eklentiler uygulamanın içinde, aynı yetkiyle çalışır ve tüm veriye erişebilir. Yalnızca güvendiğiniz kaynaklardan eklenti kurun.

Çalışan bir örnek depoda: [`samples/Arma.Plugins.Sample`](../samples/Arma.Plugins.Sample). Menüye bağlantı ve sayfa, izin, kişi kartı sekmesi, pano kutucuğu, API ucu ve kişi birleştirme olayını dinlemeyi gösterir.

## Proje

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <ArmaDir>C:\inetpub\arma-crm</ArmaDir> <!-- kurulu Arma klasörü -->
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <Reference Include="$(ArmaDir)\Arma.SharedKernel.dll" Private="false" />
    <Reference Include="$(ArmaDir)\Arma.UI.dll" Private="false" />
    <Reference Include="$(ArmaDir)\Arma.Modules.Contacts.Contracts.dll" Private="false" />
  </ItemGroup>
  <ItemGroup>
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Arma derlemelerine `Private="false"` ile başvurun: eklenti klasörüne kopyalanmazlar, çalışırken uygulamadakiler kullanılır. Arma'da zaten olan bir paketi (EF Core, Wolverine, ASP.NET Core) eklentiyle birlikte dağıtmanız gerekmez; dağıtsanız da uygulamadaki sürüm kullanılır. Eklentinin kendine ait bağımlılıkları (örneğin bir kargo firmasının SDK'sı) derleme çıktısıyla aynı klasörde durur ve oradan yüklenir.

Kullanabileceğiniz derlemeler:

| Derleme | İçerik |
| --- | --- |
| `Arma.SharedKernel` | `IModule`, izinler, menü, pano, zamanlanmış iş, portal, KVKK, içe aktarma sözleşmeleri |
| `Arma.UI` | Kart, tablo, düğme, rozet, form, iletişim kutusu bileşenleri |
| `Arma.Infrastructure` | Modül veritabanı bağlamı, denetim izi, Wolverine yardımcıları |
| `Arma.Modules.*.Contracts` | Modüllerin dışa açtığı servisler ve olaylar (kişi dizini, zaman çizelgesi, iletiler, kullanıcılar) |

Modüllerin kendi derlemelerine (`Arma.Modules.Giving` gibi) başvurmayın; bunlar iç yapıdır ve sürümler arasında değişebilir.

## plugin.json

```json
{
  "id": "kurum.kargo-takip",
  "name": "Kargo takibi",
  "version": "1.2.0",
  "assembly": "Kurum.KargoTakip.dll",
  "author": "Kurum Bilgi İşlem",
  "description": "Bağış paketlerinin kargo durumunu kişi kartında gösterir.",
  "minimumArmaVersion": "1.0"
}
```

- `id`: küçük harf, rakam, nokta ve tire; kurulumda tekildir. Eklentiyi kapatırken bu yazılır.
- `assembly`: klasördeki ana derlemenin dosya adı.
- `minimumArmaVersion`: daha eski bir Arma'da eklenti yüklenmez, Yönetim → Eklentiler ekranında nedeni görünür.

## Giriş noktası

Ana derlemede `IModule` uygulayan ve parametresiz kurucusu olan her `public` sınıf bir modül olarak çalıştırılır.

```csharp
public sealed class KargoModule : IModule, INavigationContributor
{
    public const string ReadPermission = "kargo.read";

    public string Name => "Kargo takibi";                         // kurulumda tekil olmalı
    public Assembly PagesAssembly => typeof(KargoModule).Assembly; // Blazor sayfaları varsa
    public IEnumerable<PermissionDefinition> Permissions => [new(ReadPermission, "Kargo", "Kargo durumunu görme")];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<INavigationContributor>(this);
        services.AddSingleton<IContactPanel, KargoPanel>();
        services.AddScoped<KargoService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/plugins/kargo/{id:guid}", (Guid id, KargoService s) => s.GetAsync(id))
            .RequireAuthorization(ReadPermission);

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;

    public IEnumerable<NavigationItem> GetNavigationItems() => [new("Kargo", "plugins/kargo", 500, ReadPermission)];
}
```

İzinler rol ekranında kendi gruplarıyla listelenir; Yönetici rolü hepsine sahiptir. Diğer rollere yöneticinin vermesi gerekir. Sayfalarda `@attribute [Authorize(Policy = KargoModule.ReadPermission)]`, uçlarda `RequireAuthorization(...)` kullanın; servisler izin denetlemez. Eklenti uçlarını `/api/v1/plugins/{eklenti}/...` altında, sayfalarını `/plugins/{eklenti}` altında toplayın. Menü bağlantısında 900 ve üstü sıra, bağlantıyı "Yönetim" başlığının altına koyar.

## Genişletme noktaları

Servis olarak kaydettiğiniz şu arayüzler uygulamanın ilgili yerinde kendiliğinden görünür:

| Arayüz | Nerede görünür |
| --- | --- |
| `INavigationContributor` | Kenar çubuğu |
| `IContactPanel` (Contacts.Contracts) | Kişi kartında sekme; bileşen `ContactId` parametresini alır |
| `IDashboardContributor` | Panoda kutucuk ve sütun grafik |
| `IContactCriteriaProvider` (Contacts.Contracts) | Gelişmiş aramada ölçüt; kayıtlı aramalar ve otomasyon kuralları bunu kullanır |
| `IPortalSection` | Bağışçı portalında bölüm; bileşen `ContactId` parametresini alır |
| `IScheduledJob` | Dakikada bir çalışan iş zamanlayıcısı |
| `IPersonalDataSource` | KVKK veri indirme ve anonimleştirme; kişi verisi tutan her eklenti bunu uygulamalı |
| `IDataImporter` | Yönetim → Veri aktarımı ekranında yeni içe aktarma türü |
| `ICampaignSource` | Kampanyaya bağlanabilen kayıt türü |
| `IPaymentProvider` | Ödeme sağlayıcısı; kaydetmeye gerek yok, sınıf kendiliğinden bulunur ve "Makbuz ve ödeme" ekranında listelenir (bkz. [odeme.md](odeme.md)) |
| `IPaymentHandler<TContext, TResult>` | Ödeme zincirine işleyici (tutar sınırı, dolandırıcılık kontrolü gibi) |
| `IEInvoiceProvider` | e-Fatura / e-Arşiv entegratörü; sınıf kendiliğinden bulunur ve "Fatura ayarları" ekranında listelenir (bkz. [efatura.md](efatura.md)) |
| `IEInvoiceHandler` | Fatura gönderim zincirine işleyici (onay, ek kural, kayıt gibi) |

**Olayları dinlemek:** Modüller olaylarını Wolverine ile yayımlar ve olay tipleri Contracts derlemelerindedir. Eklentide `Handle` metodu olan bir sınıf yeterli; Arma onu kendiliğinden bulur:

```csharp
public static class ContactsMergedHandler
{
    public static Task Handle(ContactsMerged message, KargoService service, CancellationToken ct) =>
        service.MoveContactAsync(message.DuplicateId, message.PrimaryId, ct);
}
```

Kişi birleştirildiğinde (`ContactsMerged`) eklentinin kişiye bağlı kayıtlarını asıl kayda taşıması gerekir. Kişinin zaman çizelgesine kayıt düşmek için `RecordTimelineEntry`, e-posta göndermek için `SendTransactionalEmail` iletisini yayımlayın. Zaman çizelgesine düşen kayıtlar webhook'lara ve otomasyonlara da ulaşır.

## Veritabanı

Kendi tablosu olan eklenti, yerleşik modüller gibi kendi şemasını kullanır:

```csharp
services.AddModuleDbContext<KargoDbContext>(configuration, "kargo");

public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
{
    await using var scope = services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<KargoDbContext>().Database.MigrateAsync(cancellationToken);
}
```

- Varlıklar `Entity`'den türer; `TenantId`, oluşturma ve güncelleme bilgileri ile silme işareti kendiliğinden yazılır.
- Her tabloya kurum filtresi (`HasQueryFilter(x => x.TenantId == tenantId && !x.IsDeleted)`) ve göçte satır düzeyi güvenlik (`ENABLE/FORCE ROW LEVEL SECURITY` ve `tenant_isolation` politikası) ekleyin. Yerleşik modüllerin göçleri örnek olarak kullanılabilir.
- Değişikliklerin denetim izine düşmesi için `OnModelCreating` içinde `modelBuilder.MapAuditLog()` çağırın.
- Göçler `dotnet Arma.Host.dll migrate` ile (Docker'da `migrate` servisi, IIS'te kurulum betiği) yerleşik modüllerle birlikte uygulanır.

## Görünüm

`Arma.UI` bileşenlerini (`Card`, `Table`, `Button`, `Badge`, `Input`, `Dialog`...) kullanın; eklenti uygulamanın geri kalanıyla aynı görünür ve açık/koyu temaya uyar. Uygulamanın stil dosyası derlenirken yalnızca Arma'nın kendi kodundaki Tailwind sınıfları üretilir: yerleşik sayfalarda kullanılmayan bir sınıf eklentide çalışmaz. Gerekirse bileşen içinde `<style>` kullanın. Eklentinin `wwwroot` klasöründeki dosyalar sunulmaz; görsel ya da betik gerekiyorsa uç noktadan döndürün.

Sayfalar varsayılan olarak sunucuda bir kez çizilir; düğme tıklaması gibi etkileşim için sayfaya `@rendermode InteractiveServer` ekleyin.

## Kurulum ve kapatma

1. Derleme çıktısını (`plugin.json`, ana derleme ve kendi bağımlılıkları) `plugins/{id}/` klasörüne kopyalayın:
   - Docker: `deploy/plugins/{id}/`, ardından `docker compose up -d --force-recreate migrate arma`
   - IIS: `C:\inetpub\arma-crm\plugins\{id}\`, ardından uygulama havuzunu yeniden başlatın. Güncelleme betiği `plugins` klasörüne dokunmaz.
2. **Yönetim → Eklentiler** ekranında eklentinin "Etkin" göründüğünü kontrol edin. Yüklenemeyen eklenti uygulamayı durdurmaz; nedeni bu ekranda ve uygulama günlüğünde yazar.
3. Kapatmak için kimliği `Arma__Plugins__Disabled__0=kurum.kargo-takip` (birden fazlaysa `__1`, `__2`...) ayarına yazıp yeniden başlatın. Hiçbir eklenti yüklenmesin diye: `Arma__Plugins__Enabled=false`. Klasör yerini değiştirmek için: `Arma__Plugins__Path`.

Geliştirirken `Arma__Plugins__Path` ayarını, içinde `{id}` klasörü bulunan bir klasöre verip Arma'yı yerelde çalıştırabilirsiniz; depodaki entegrasyon testleri örnek eklentiyi bu şekilde yükler.
