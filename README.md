# Arma CRM

Türkiye için geliştirilen, kurumun kendi sunucusuna kurulan, genel amaçlı açık mimarili CRM.
Ürün ve teknik plan: [Arma CRM: Ürün ve Teknik Plan](https://claude.ai/code/artifact/dbdc5b76-403a-4e02-ae8f-15fadaf086ba).

Tamamlanan: çözüm iskeleti, `Arma.UI` bileşen kütüphanesi, PostgreSQL + kurum yalıtımı, kimlik doğrulama
(parola, zorunlu 2FA, roller ve izinler, API istemcileri), denetim izi, Kişiler modülü (kişi/kurum/hane kartı,
adresler, etiketler, gruplar, ilişkiler, mükerrer bulma ve birleştirme, CSV içe/dışa aktarma), Aktiviteler modülü
(not, arama, toplantı, görev, kişi zaman çizelgesi, Görevlerim), Bağışlar modülü (fonlar, sıralı makbuz,
iade, kamuya açık bağış formu, iyzico ve PayTR), İletişim modülü (e-posta, SMS, toplu ileti, şablonlar,
ticari ileti izinleri, İYS bildirimi, abonelik iptali), KVKK modülü (sürümlü aydınlatma ve açık rıza metinleri,
rıza kayıtları, ilgili kişi başvuruları, veri dışa aktarma, anonimleştirme, kişisel veri envanteri), Wolverine ile modüller arası olaylar, testler ve
kurulum paketi.

## Teknoloji

| Katman | Seçim |
| --- | --- |
| Çalışma zamanı | .NET 10 (LTS) |
| Arayüz | Blazor Web App (Interactive Server), Tailwind CSS v4, shadcn/ui tarzı `Arma.UI` |
| Veritabanı | PostgreSQL (EF Core 10 + Npgsql), modül başına şema, Row Level Security |
| Mesajlaşma | Wolverine: modüller arası olaylar, PostgreSQL'de kalıcı kuyruk ve outbox |
| Gözlemlenebilirlik | OpenTelemetry, yerelde .NET Aspire paneli |
| Test | xUnit v3, bUnit, NetArchTest, Shouldly |
| Kurulum | Docker Compose paketi (`deploy/`) |

## Klasör yapısı

```
src/
  Arma.Host/              ASP.NET Core giriş noktası: Blazor, API, modül kaydı, Tailwind derlemesi
  Arma.UI/                shadcn/ui tarzı Razor bileşenleri (Button, Card, Input, Table, Badge, Alert...)
  Arma.SharedKernel/      Entity, Result, kurum bağlamı, IModule sözleşmesi, izin tanımları
  Arma.Infrastructure/    Modüllerin ortak altyapısı: kiracı bağlantısı, kayıt damgaları, geçerli kullanıcı, mesajlaşma
  Arma.ServiceDefaults/   OpenTelemetry, sağlık kontrolleri, dayanıklı HTTP istemcileri
  Arma.AppHost/           .NET Aspire ile yerel geliştirme ortamı
  Modules/
    Contacts/             Kişiler modülü (Domain, Application, Infrastructure, Web) + Contracts
    Activities/           Not, arama, toplantı, görev; kişi zaman çizelgesi; Contracts: zaman çizelgesi mesajı
    Giving/               Bağışlar, fonlar, makbuz, çevrim içi bağış formu ve ödeme kuruluşları
    Privacy/              KVKK: aydınlatma metinleri, açık rıza, ilgili kişi başvuruları, anonimleştirme, veri envanteri
    Comms/                E-posta (SMTP), SMS (Netgsm), toplu ileti, ileti izinleri ve İYS; Contracts: bilgilendirme e-postası
    Identity/             Kullanıcılar, roller, izinler, 2FA, API istemcileri (Identity + OpenIddict); Contracts: kullanıcı listesi
    Audit/                Denetim izi tablosu, arama ekranı; Contracts: kayıt geçmişi bileşeni
tests/
  Arma.Modules.Contacts.Tests/   TCKN/VKN kontrolleri, kişi kuralları
  Arma.UI.Tests/                 bileşen testleri (bUnit)
  Arma.Architecture.Tests/       modül sınırı kuralları
  Arma.IntegrationTests/         uygulamayı gerçek PostgreSQL ile çalıştıran testler
deploy/                   Linux kurulum paketi (Docker Compose + install.sh)
```

## Geliştirme

Gereken: .NET 10 SDK. Tailwind ilk derlemede `.tools/` altına kendiliğinden indirilir, Node.js gerekmez.

**Aspire ile (Docker gerekir):**

```bash
dotnet run --project src/Arma.AppHost
```

PostgreSQL konteynerini ve uygulamayı birlikte başlatır, şemayı günceller ve Aspire panelini açar.

**Kendi PostgreSQL'inizle:** bağlantı dizesini depoya değil, kullanıcı gizli ayarlarına yazın:

```bash
dotnet user-secrets set "ConnectionStrings:arma" "Host=localhost;Database=arma;Username=arma;Password=<parola>" --project src/Arma.Host
dotnet run --project src/Arma.Host
```

**Testler:**

```bash
dotnet test --solution Arma.slnx
```

Entegrasyon testleri PostgreSQL ister; her test sınıfı geçici bir veritabanı açıp siler. Bağlantıyı
veritabanı oluşturma yetkisi olan bir kullanıcıyla verin, verilmezse bu testler atlanır:

```bash
export ARMA_TEST_CONNECTION="Host=localhost;Database=postgres;Username=arma;Password=<parola>"
```

**Yeni migration (örnek: Kişiler modülü):**

```bash
dotnet tool restore
dotnet ef migrations add <Ad> -p src/Modules/Contacts/Arma.Modules.Contacts -s src/Modules/Contacts/Arma.Modules.Contacts -o Infrastructure/Migrations
```

## Kurulum (Linux sunucu)

```bash
cd deploy
./install.sh
```

İlk çalıştırmada `.env` dosyasını üretir (veritabanı parolası ve kurum kimliği), imajı derler,
önce `migrate` servisini, sonra uygulamayı başlatır. Güncellemede aynı komut yeterlidir.
`.env` dosyasını yedekleyin.

## Kimlik ve yetki

- İlk açılışta `/setup` ekranı ilk yöneticiyi oluşturur. Kullanıcı olmadan başka sayfa açılmaz.
- Yönetici rolündekiler iki adımlı doğrulamayı (TOTP: Google/Microsoft Authenticator) açmadan çalışamaz.
  Her rol için 2FA zorunluluğu Roller ekranından açılabilir.
- 5 hatalı parola denemesinde hesap 15 dakika kilitlenir. Oturum 60 dakika işlem yapılmazsa kapanır
  (`Arma:Security:SessionTimeoutMinutes`).
- Yetki izin bazlıdır (`contacts.read`, `users.manage` gibi). Her modül kendi izinlerini `IModule.Permissions`
  ile tanımlar; Yönetici rolü yeni modüllerin izinlerini de otomatik alır.
- Başka sistemler API'ye OAuth2 istemci kimlik bilgileriyle erişir: Yönetim > API istemcileri ekranından
  istemci oluşturulur, `POST /connect/token` (`grant_type=client_credentials`) ile belirteç alınır ve
  `Authorization: Bearer` başlığıyla gönderilir. İstemci yalnızca kendisine verilen izinleri kullanabilir.
- Oturum ve belirteç anahtarları veritabanında (`identity.data_protection_keys`) saklanır; konteyner
  yeniden başlasa da oturumlar düşmez.

## Bağış ve ödeme

- Makbuz numarası kurum başına, her yıl 1'den başlayarak boşluksuz artar (`{önek}{yıl}-000001`).
  Kesilmiş makbuzun tutarı, tarihi ve bağışçısı değişmez; iptal edilen numara tekrar kullanılmaz.
- Kamuya açık bağış formu `/bagis` adresindedir. Ödeme kuruluşu `Giving:Payments:Provider` ile seçilir
  (`none`, `iyzico`, `paytr`); anahtarlar ortam değişkeni veya `.env` ile verilir, veritabanına yazılmaz.
- Ödeme sonucu yalnızca imzası doğrulanmış (PayTR) veya kuruluştan sorgulanmış (iyzico) bildirimle
  tamamlanır; aynı bildirim tekrar gelirse sonuç değişmez.
- Geliştirmede `Giving__Payments__Provider=test` sahte ödeme sayfasını açar; canlı ortamda kullanılamaz.

## E-posta, SMS ve İYS

- Sağlayıcılar `.env` ile seçilir: e-posta için `ARMA_EMAIL_PROVIDER=smtp` ve `SMTP_*`, SMS için
  `ARMA_SMS_PROVIDER=netgsm` ve `NETGSM_*`. Parolalar veritabanına yazılmaz; Yönetim > E-posta ve SMS
  ekranında durum görülür ve deneme gönderimi yapılır.
- İletiler kalıcı kuyruğa alınır ve arka planda gönderilir; uygulama yeniden başlasa da kaybolmaz.
  Gönderilen ileti kişinin zaman çizelgesine düşer.
- Ticari ileti (tanıtım, kampanya, bağış çağrısı) yalnızca o kanaldan izni onaylı kişiye gider; izin gönderim
  anında yeniden kontrol edilir. E-postaya abonelik iptal bağlantısı ve tek tık iptal başlığı (RFC 8058)
  eklenir; bunun için `ARMA_PUBLIC_URL` tanımlı olmalıdır. Makbuz gibi bilgilendirme iletileri izinden bağımsızdır.
- `IYS_ENABLED=true` iken her izin değişikliği (elle, form, abonelik iptali) İYS'ye bildirilir; başarısız
  bildirimler ekrandan yeniden denenebilir.
- Çevrim içi bağış tamamlanınca bağışçıya teşekkür e-postası gider.
- Geliştirmede `Comms__Email__Provider=test` ve `Comms__Sms__Provider=test` iletileri yalnızca bellekte tutar.

## KVKK

- Aydınlatma ve açık rıza metinleri taslak olarak yazılır, yayımlanınca sürüm numarası alır ve `/kvkk/{adres}`
  sayfasında görünür. Yayımlanmış sürüm ve rıza kayıtları değiştirilemez (veritabanı tetikleyicisi reddeder);
  rıza, verildiği sürümle birlikte saklanır, metin değişince eski sürüme verilmiş rıza ekranda işaretlenir.
- İlgili kişi başvuruları `/kvkk/basvuru` formundan veya elle kaydedilir; son gün başvuru tarihinden 30 gün
  sonradır, süresi geçenler listede işaretlenir. Başvuru, verilen yanıt yazılmadan kapatılamaz.
- Kişi kartındaki KVKK sekmesinden kişinin tüm modüllerdeki verisi tek JSON dosyasında indirilir ve kişi
  anonimleştirilir. Anonimleştirme geri alınamaz: ad, iletişim, kimlik numarası, adres, etiket, ilişki, not ve
  ileti içerikleri silinir; bağış tutarı ve makbuz Vergi Usul Kanunu gereği kalır. Kurum kayıtları anonimleştirilmez.
- Kişi verisi tutan her modül `IPersonalDataSource` uygular: envanter satırları, dışa aktarma ve anonimleştirme
  bu arayüzden toplanır. Yeni modül eklendiğinde başvuru akışı ve envanter kendiliğinden genişler.
- Kişisel veri envanteri ekranı VERBİS kaydına başlangıç olarak CSV verir.
- Denetim izi, işlem güvenliği ve ispat için anonimleştirmeden sonra da saklanır; anonimleştirmenin kendisi
  denetim izine değer yazmadan kaydedilir.

## Pano, arama ve formlar

- Ana sayfa pano: her modül `IDashboardContributor` ile kutucuk ve grafik ekler; kullanıcı yalnızca yetkisi
  olan modüllerin kutucuklarını görür.
- Gelişmiş arama (`/contacts/search`): kişi alanlarıyla birlikte modüllerin `IContactCriteriaProvider` ile
  eklediği ölçütler (son bağış, toplam bağış, ileti izni, açık rıza, açık görev…) birleştirilir. Aramalar
  kaydedilir, sonuç CSV olarak indirilir veya bir gruba eklenir; toplu ileti bu gruba gönderilir.
- Formlar (`/forms`): alanları ekrandan tanımlanan web formları `/f/{adres}` adresinde yayımlanır ve
  `<iframe>` ile başka sitelere gömülebilir. Ad, soyad ve e-posta kişi kaydına eşlenirse yanıt kişiye bağlanır
  (aynı e-posta varsa o kişi kullanılır), etiket ve grup eklenir, e-posta/SMS izni ve açık rıza işlenir,
  zaman çizelgesine yazılır. Gömülebilmesi için yalnızca `/f/` altındaki sayfalar çerçeve kısıtı başlığı
  (`X-Frame-Options`, `frame-ancestors`) taşımaz; bu sayfalarda antiforgery yerine gizli bot alanı kullanılır.

## Denetim izi

- `Entity` türevlerindeki her ekleme, değişiklik ve silme, aynı veritabanı işleminde `audit.audit_log`
  tablosuna yazılır (kim, ne zaman, hangi IP, alan bazında eski ve yeni değer).
- `[SensitiveData]` işaretli alanların (TCKN gibi) değeri yazılmaz, yalnızca değiştiği görülür.
  Maskesiz görüntüleme, dışa aktarma, içe aktarma ve birleştirme ayrıca kaydedilir.
- Tablo yalnızca eklemeye açıktır: bir tetikleyici güncelleme ve silmeyi reddeder.

## Mimari kurallar

- Her modül kendi PostgreSQL şemasını kullanır; başka modülün tablosuna yazmaz.
- Modüller birbirine yalnızca `*.Contracts` projeleri üzerinden erişir.
- Bir modülün diğerlerini etkileyen değişikliği (örneğin kişi birleştirme) Wolverine olayı olarak, veritabanı
  değişikliğiyle aynı işlemde `wolverine` şemasına yazılır ve arka planda işlenir; uygulama çökse de kaybolmaz.
  İşlenemeyen mesajlar `wolverine.wolverine_dead_letters` tablosunda kalır.
- Başka bir modül, kişinin zaman çizelgesine `RecordTimelineEntry` mesajıyla kayıt düşer (bağış alındı gibi);
  aynı kaynak tekrar gelirse kayıt güncellenir.
- Her tabloda `tenant_id` vardır. EF sorgu filtresine ek olarak PostgreSQL Row Level Security
  politikası, `app.tenant_id` oturum değişkenine göre başka kurumun satırını gizler.
- Şema değişiklikleri uygulama açılışında değil `dotnet Arma.Host.dll migrate` komutuyla uygulanır
  (geliştirmede `Arma:MigrateOnStartup` ile otomatik).
- Arayüz kodu yalnızca `Arma.UI` bileşenlerini kullanır; sayfalarda yerleşim dışında ham Tailwind sınıfı
  kullanılmaz.

## Sıradaki adımlar (Faz 0'dan kalanlar)

- Hassas alanların veritabanında şifrelenmesi.
- İl-ilçe-mahalle referans verisi (şu an yalnızca 81 il listesi var, ilçe serbest metin).
- `Arma.UI`: Dialog, Select, Combobox, Dropdown Menu, Sheet, Tabs, Toast, Data Table.
- Windows Server (IIS) kurulum paketi.
