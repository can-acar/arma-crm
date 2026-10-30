# Arma CRM

Türkiye için geliştirilen, kurumun kendi sunucusuna kurulan, genel amaçlı açık mimarili CRM.
Ürün ve teknik plan: [Arma CRM: Ürün ve Teknik Plan](https://claude.ai/code/artifact/dbdc5b76-403a-4e02-ae8f-15fadaf086ba).

Bu depo şu an **Faz 0 (Temel)** aşamasında: çözüm iskeleti, `Arma.UI` bileşen kütüphanesi,
ilk modüller (Kimlik, Kişiler), PostgreSQL + kurum yalıtımı, testler ve kurulum paketi.

## Teknoloji

| Katman | Seçim |
| --- | --- |
| Çalışma zamanı | .NET 10 (LTS) |
| Arayüz | Blazor Web App (Interactive Server), Tailwind CSS v4, shadcn/ui tarzı `Arma.UI` |
| Veritabanı | PostgreSQL (EF Core 10 + Npgsql), modül başına şema, Row Level Security |
| Gözlemlenebilirlik | OpenTelemetry, yerelde .NET Aspire paneli |
| Test | xUnit v3, bUnit, NetArchTest, Shouldly |
| Kurulum | Docker Compose paketi (`deploy/`) |

## Klasör yapısı

```
src/
  Arma.Host/              ASP.NET Core giriş noktası: Blazor, API, modül kaydı, Tailwind derlemesi
  Arma.UI/                shadcn/ui tarzı Razor bileşenleri (Button, Card, Input, Table, Badge, Alert...)
  Arma.SharedKernel/      Entity, Result, kurum bağlamı, IModule sözleşmesi
  Arma.ServiceDefaults/   OpenTelemetry, sağlık kontrolleri, dayanıklı HTTP istemcileri
  Arma.AppHost/           .NET Aspire ile yerel geliştirme ortamı
  Modules/
    Identity/             Kullanıcılar, roller, giriş/çıkış (ASP.NET Core Identity, çerez oturumu)
    Contacts/             Kişiler modülü (Domain, Application, Infrastructure, Web) + Contracts
tests/
  Arma.Modules.Contacts.Tests/   TCKN/VKN kontrolleri, kişi kuralları
  Arma.UI.Tests/                 bileşen testleri (bUnit)
  Arma.Architecture.Tests/       modül sınırı kuralları
  Arma.Host.Tests/               kimlik doğrulama uçtan uca testleri (veritabanı gerekmez)
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

**İlk kullanıcı:** uygulamaya giriş için bir yönetici oluşturun (parola standart girişten sorulur, en az 12 karakter):

```bash
dotnet run --project src/Arma.Host -- create-admin admin@ornek.com.tr "Ad Soyad"
```

**Testler:**

```bash
dotnet test --solution Arma.slnx
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

İlk yöneticiyi oluşturmak için:

```bash
docker compose run --rm -it migrate create-admin admin@ornek.com.tr "Ad Soyad"
```

## Mimari kurallar

- Her modül kendi PostgreSQL şemasını kullanır; başka modülün tablosuna yazmaz.
- Modüller birbirine yalnızca `*.Contracts` projeleri üzerinden erişir.
- Her tabloda `tenant_id` vardır. EF sorgu filtresine ek olarak PostgreSQL Row Level Security
  politikası, `app.tenant_id` oturum değişkenine göre başka kurumun satırını gizler.
- Şema değişiklikleri uygulama açılışında değil `dotnet Arma.Host.dll migrate` komutuyla uygulanır
  (geliştirmede `Arma:MigrateOnStartup` ile otomatik).
- Her sayfa ve API uç noktası varsayılan olarak oturum ister (yetkilendirme fallback politikası).
  Herkese açık olanlar (giriş sayfası, sağlık kontrolleri, statik dosyalar) `AllowAnonymous` ile işaretlenir.
  Oturumsuz API çağrıları yönlendirilmez, `401` döner.
- Oturum çerezlerini koruyan Data Protection anahtarları `identity.data_protection_keys` tablosundadır;
  konteyner yenilense de oturumlar düşmez.
- Arayüz kodu yalnızca `Arma.UI` bileşenlerini kullanır; sayfalarda yerleşim dışında ham Tailwind sınıfı
  kullanılmaz.

## Sıradaki adımlar (Faz 0'dan kalanlar)

- Kimlik: 2FA (TOTP) sayfaları, kullanıcı yönetimi ekranı, parola sıfırlama (e-posta altyapısı gerekiyor).
- Kimlik: dış istemciler için OpenIddict ile token tabanlı API erişimi.
- Kimlik tabloları için Row Level Security (şu an yalnızca EF sorgu filtresi var) ve kayıtların
  `created_by` / `updated_by` alanlarının oturumdaki kullanıcıdan doldurulması.
- `Arma.UI`: Dialog, Select, Combobox, Dropdown Menu, Sheet, Tabs, Toast, Data Table.
- Wolverine ile modüller arası olaylar ve outbox.
- Windows Server (IIS) kurulum paketi.
