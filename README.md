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

## Kurulum (Windows Server / IIS)

Paketi üretmek için (.NET 10 SDK ile, herhangi bir işletim sisteminde):

```bash
deploy/windows/build-package.sh 1.0.0   # deploy/out/arma-crm-iis-1.0.0.zip
```

Sunucuda ASP.NET Core 10 Hosting Bundle ve PostgreSQL 18 kurulu olmalı. Paketi açıp yönetici PowerShell'de:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-iis.ps1 -CreateDatabase
```

Betik IIS, WebSocket ve Uygulama Başlatma özelliklerini açar; ilk kurulumda `C:\ProgramData\Arma\arma.env` dosyasını
(kurum kimliği, `-CreateDatabase` ile süper kullanıcı olmayan `arma` rolü ve parolası) üretir; dosyaları kopyalar, `migrate`
ile şemayı günceller ve sürekli açık bir uygulama havuzu ile siteyi kurar. Ayarlar havuzun ortam değişkenlerine yazılır,
site klasöründe tutulmaz. Güncellemede yeni paketle aynı komut (`-CreateDatabase` olmadan) yeterlidir; ayrıntılar
`deploy/windows/KURULUM.txt` içinde.

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

## Üyelik

- Üyelik türleri (asıl, fahri, öğrenci gibi) süre, dönem aidatı ve ek süreyle tanımlanır; süre 0 ise üyelik
  ömür boyudur. Üye numarası kurum içinde sırayla verilir.
- Durum tarihlerden hesaplanır: dönem içinde "Aktif", dönem bitip ek süre dolmadan "Aidat bekliyor", sonra
  "Süresi doldu". Ek süre içinde yenilenen üyelik kesintisiz devam eder, sonra yenilenen bugünden başlar.
- Aidat ödemeleri üyeliğe ve döneme bağlı tutulur; "Aidatlar" ekranı tarih aralığındaki tahsilatı toplar.
  Üye kartı kredi kartı boyutunda yazdırılır, karekodu üyelik kaydını açar.
- Dönemi yaklaşan üyelere yenileme hatırlatması e-postası gider (`Membership__ReminderDays`, varsayılan 30;
  0 kapatır). Zamanlanmış işler `IScheduledJob` ile yazılır ve dakikada bir yoklanır; birden çok sunucu
  çalışıyorsa yalnızca birinde `Arma__Jobs__Enabled=true` bırakın.

## Etkinlikler

- Etkinlik taslak olarak hazırlanır, yayımlanınca `/e/{adres}` sayfasından kayıt alınır (ad, soyad, e-posta;
  kişi e-postayla bulunur veya açılır). Kontenjan etkinlik ve bilet türü bazında konabilir; dolunca kayıtlar
  yedek listeye alınır, bir kayıt iptal edilince sıradaki kişi kesin kayda geçer ve e-postayla bilgilendirilir.
  Aynı anda gelen kayıtlar kontenjanı aşmasın diye etkinlik satırı kilitlenir.
- Her kayda tahmin edilemeyen bir bilet kodu verilir; onay e-postasında bilet bağlantısı (`/e/bilet/{kod}`,
  `ARMA_PUBLIC_URL` gerekir) ve karekod bulunur. Giriş ekranında kod kamerayla (tarayıcı destekliyorsa),
  el okuyucusuyla veya elle okutulur; ikinci okutma ve yedekteki bilet reddedilir.
- Ücretli biletin ödemesi personel tarafından işaretlenir (girişte veya havaleyle). Çevrim içi kartla bilet
  ödemesi henüz yok.
- Giriş yapan katılımcı `/e/bilet/{kod}/belge` adresinden katılım belgesini yazdırabilir. Etkinlikten bir gün
  önce kayıtlılara hatırlatma e-postası gider; etkinlik iptal edilirse herkese bildirim gider.

## Satış hattı ve teklifler

- Fırsatlar (başlık, müşteri, tutar, beklenen kapanış, sorumlu) aşamalı bir panoda izlenir; kart sürüklenerek
  aşama ve sıra değiştirilir. Kaybedildi aşamasına taşınan fırsat için neden sorulur. Panonun üstünde açık
  toplam ve aşama olasılığıyla ağırlıklı tahmin görünür; kapanmış fırsatlar 30 gün sonra panodan kalkar.
- Aşamalar ve olasılıkları Yönetim > Satış ayarları'ndan değiştirilir; birden fazla satış hattı tanımlanabilir.
  İçinde fırsat olan aşama silinemez.
- Teklifler `T{yıl}-{sıra}` numarasıyla kesilir. Her kalemde miktar, birim, birim fiyat, iskonto ve KDV (%0, 1,
  10, 20) vardır; tutarlar kuruşa yuvarlanır. Fırsattan hazırlanan teklif kabul edilince fırsat kazanıldı,
  reddedilince kaybedildi olur. Yazdırma sayfası tarayıcıdan PDF olarak kaydedilebilir.
- Fırsat ve teklif hareketleri kişinin zaman akışına düşer; gelişmiş aramada "açık fırsatı olan", "şu tarihten
  sonra satış kazanılan" ve "hiç fırsatı olmayan" ölçütleri vardır.

## Otomasyon

- Kurallar "şu olunca şunları yap" biçimindedir. Tetikleyici ya bir modülün kişinin zaman akışına düştüğü kayıttır
  (bağış, üyelik, etkinlik, satış, form; isteğe bağlı olarak konusu belirli bir metni içerenler) ya da kişinin
  Gelişmiş arama'da kaydedilmiş bir aramaya girmesidir (15 dakikada bir denetlenir).
- İşlemler: etiket ekleme, gruba ekleme, görev açma (sorumlu ve süreyle) ve bilgilendirme e-postası
  (`{{ad}}`, `{{soyad}}`, `{{adsoyad}}`, `{{eposta}}` alanlarıyla). Otomasyon e-postasında ticari ileti izni
  aranmaz; kampanya içeriği için Toplu ileti kullanılmalıdır.
- Bir kural aynı kişiye aynı olay için bir kez uygulanır. Kayıtlı arama kuralı açılırken aramaya zaten uyan kişiler
  istenirse atlanır. Kural ekranında son 100 çalışma görünür.

## Bağış taahhütleri

**Bağışlar → Taahhütler** bağışçının ileride ödemeyi üstlendiği tutarları izler ("12 ay boyunca ayda 1.000 TL"). Taahhüt kişi kartının Bağışlar sekmesinden ya da listeden eklenir: toplam tutar, para birimi, sıklık (tek sefer, aylık, üç ayda bir, yıllık), taksit sayısı, ilk vade ve isteğe bağlı fon.

- Taksitler saklanmaz, tutardan hesaplanır: tutar eşit bölünür, kuruş farkı son taksite eklenir. Taahhüde bağlanan tamamlanmış bağışlar vade sırasıyla taksitleri kapatır; iade edilen bağış ödemeden düşer.
- Bağış kaydederken bağışçının açık taahhüdü seçilebilir; taahhüt sayfasındaki **Ödeme kaydet** tutarı sıradaki taksitle, fonu taahhüdün fonuyla doldurur. Havale eşleştirmede bağışçının kalan borcu olan en eski taahhüdü varsa havale otomatik olarak ona bağlanır.
- Vadesi geçmiş taksiti ödenmemiş taahhüt "Gecikmiş" görünür. Panoda "Geciken taahhüt" kutucuğu, gelişmiş aramada "Ödenmemiş taahhüdü var" ve "Taahhüt taksiti gecikmiş" ölçütleri bulunur.
- İptal edilen taahhüde yeni ödeme bağlanamaz, bağlı ödemeler kalır. Ödemesi olmayan taahhüt silinebilir. Mükerrer birleştirmede taahhütler asıl kişiye taşınır; KVKK dışa aktarması taahhütleri içerir, anonimleştirme notları siler.

## Havale/EFT eşleştirme ve yıl sonu makbuzları

- Bağışlar > Havale eşleştirme sayfasına internet şubesinden indirilen ekstre yüklenir: Excel (.xlsx), CSV
  (UTF-8 veya Windows-1254) ya da MT940. Excel ve CSV'de başlık satırı "Tarih", "Tutar"/"Alacak", "Açıklama"
  sözcükleriyle bulunur. Yalnızca gelen hareketler alınır; aynı hareket ikinci kez yüklenirse atlanır.
- Bağışçı önerisi önce aynı IBAN'dan daha önce onaylanmış havaleye, sonra açıklamadaki ada bakar (tam ad
  eşleşmesi, Türkçe karaktersiz yazımlar dahil). Açıklamada fon adı geçiyorsa fon da önerilir. Öneriler tek tek
  veya topluca onaylanır; onaylanan hareket "Havale / EFT" yöntemli bağış olur.
- Kişi araması artık Türkçe karakterlere duyarsızdır: "SUKRU OZTURK" araması "Şükrü Öztürk"ü bulur.
- Bağışlar sayfasındaki "Toplu makbuz" seçilen yılda makbuzu kesilmemiş tamamlanmış bağışlara bağış tarihi
  sırasıyla makbuz keser. Kişi kartındaki "Yıllık özet" bağışçının o yılki bağışlarını yazdırılabilir
  dökümde gösterir.

## Kampanyalar

- Kampanya bir amaç altında yürütülen çalışmayı toplar (yardım kampanyası, üye kazanımı gibi): tarih aralığı,
  isteğe bağlı TL hedefi, sorumlusu ve durumu vardır.
- Kampanyaya bağış fonu, etkinlik, toplu ileti ve form bağlanır. Sonuçlar bağlı kaydın kendi modülünden
  `ICampaignSource` ile alınır; yeni bir modül bu arayüzü kaydederek kampanyalara katılır.
- Hedefe tarih aralığındaki tamamlanmış TL bağışlar ve ödenmiş bilet gelirleri sayılır. "Ulaşılan kişi" bağış yapan,
  etkinliğe kaydolan, toplu iletiyi alan veya formu dolduran tekil kişilerdir; bu kişiler tek tuşla bir gruba eklenir.
- Kullanıcı yalnızca görme yetkisi olan modüllerin kayıtlarını bağlayabilir.

## Kişi ek alanları

**Yönetim → Kişi ek alanları** sayfasından kişi kartına kurumunuza özgü alanlar eklenir: metin, sayı, tarih, evet/hayır ve seçenekli. Bir alan yalnızca kişilere, kurumlara veya hanelere gösterilecek şekilde sınırlanabilir; kapatılan alan kartta görünmez ama değerleri silinmez.

- Değerler kişi kartındaki **Ek bilgiler** kartından girilir ve kişinin `custom_fields_json` (jsonb) sütununda alan kimliğiyle saklanır. Sayılar "1.250,5" gibi Türkçe biçimde de yazılabilir.
- Her alan **Gelişmiş arama**da "Ek alanlar" grubunda ölçüt olur (metin içerir, seçenek eşittir, sayı en az, tarihten sonra, evet).
- CSV dışa aktarma her etkin alan için bir sütun ekler; içe aktarmada başlığı alan adıyla eşleşen sütunlar okunur, geçersiz değerler satır hatası olarak raporlanır.
- Değeri olan bir alanın türü değiştirilemez. Alan silinince tüm kişilerdeki değeri de silinir. Mükerrer birleştirmede asıl kaydın değerleri korunur, boş olanlar diğer kayıttan doldurulur.
- KVKK dışa aktarmasında "Ek bilgiler" bölümü yer alır; anonimleştirme tüm ek alan değerlerini siler.

## CiviCRM'den aktarım

**Yönetim → Veri aktarımı** (`contacts.import` izni) CiviCRM'den "Birincil alanlar" ile dışa aktarılmış CSV dosyalarını sırayla alır:

1. **Kişiler** (Export Contacts): kişi, kurum ve haneler; adres (State → il, City → ilçe), doğum tarihi, etiketler ve gruplar. Çöp kutusundaki kayıtlar atlanır. Arma'da aynı e-postayla kayıtlı kişi varsa yeni kayıt açılmaz, CiviCRM kimliği o kişiye bağlanır. "External Identifier" alanında geçerli bir TCKN varsa alınır.
2. **Bağışlar** (Export Contributions): yalnızca tamamlanmış ve iade edilmiş katkılar. Mali tür ("Financial Type") yayına kapalı fon olarak açılır; "Donation" fonsuz, "Member Dues" atlanır. Aktarılan bağışların makbuzu CiviCRM'de kesildiği varsayılır; yıl sonu toplu makbuza girmez.
3. **Üyelikler** (Export Memberships): üyelik türü adıyla eşleşir, yoksa 12 aylık ücretsiz tür açılır (süre ve ücreti sonra düzeltin). Tarihler olduğu gibi alınır, bitiş tarihi boşsa ömür boyu sayılır, Arma kendi üye numarasını verir.

Kayıtlar CiviCRM kimlikleriyle (`external_key`, örn. `civicrm:42`) saklanır; aynı dosya yeniden yüklenirse yalnızca yeni satırlar eklenir. Bağış ve üyelikler kişiyi bu kimlikle bulduğu için önce kişiler aktarılmalıdır; birleştirilmiş kişiler asıl kayda çözülür. Bir dosya en fazla 50 MB / 200.000 satır olabilir. Yeni bir kaynak eklemek için modül `IDataImporter` uygular.

## Vaka yönetimi

**Vakalar** menüsü sosyal yardım başvurusu, hukuki destek, şikâyet gibi bir kişi için yürütülen işleri izler. Vaka türleri **Yönetim → Vaka türleri** sayfasında adım şablonuyla tanımlanır ("Ev ziyareti, 3 gün sonra"); vaka açılınca adımlar son günleriyle birlikte oluşur, sonradan adım eklenebilir.

- Vakalar yıl bazında numaralanır (`V2026-0001`), bir sorumluya atanır ve Açık, Beklemede, Kapalı durumlarından geçer. Kapatırken sonuç yazmak zorunludur; kapalı vakada değişiklik için önce yeniden açılır.
- Kişi kartındaki **Vakalar** sekmesinden vaka açılır. Açılış, kapanış ve gizli olmayan notlar kişinin zaman çizelgesine düşer.
- **Gizli notlar** yalnızca `cases.confidential` izni olanlara gösterilir ve zaman çizelgesine yazılmaz. Personel rolü vakaları görür ve düzenler, gizli notları göremez.
- Panoda "Açık vaka" ve "Bana atanan vaka" kutucukları, gelişmiş aramada "Açık vakası var" ve "Vaka türü" ölçütleri bulunur. `GET /api/v1/cases` vaka listesini salt okunur verir.
- Mükerrer birleştirmede vakalar asıl kişiye taşınır. KVKK dışa aktarması gizli notlar dahil vakaları içerir; anonimleştirme konu, açıklama ve not metinlerini siler.

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
