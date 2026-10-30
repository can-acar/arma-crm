# Arma CRM

Türkiye için geliştirilen, kurumun kendi sunucusuna kurulan, genel amaçlı açık mimarili CRM.
Ürün ve teknik plan: [Arma CRM: Ürün ve Teknik Plan](https://claude.ai/code/artifact/dbdc5b76-403a-4e02-ae8f-15fadaf086ba).

Bu depo şu an **Faz 0 (Temel)** aşamasında: çözüm iskeleti, `Arma.UI` bileşen kütüphanesi,
ilk modül (Kişiler), PostgreSQL + kurum yalıtımı, testler ve kurulum paketi.

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
    Contacts/             Kişiler modülü (Domain, Application, Infrastructure, Web) + Contracts
tests/
  Arma.Modules.Contacts.Tests/   TCKN/VKN kontrolleri, kişi kuralları
  Arma.UI.Tests/                 bileşen testleri (bUnit)
  Arma.Architecture.Tests/       modül sınırı kuralları
deploy/                   Linux kurulum paketi (Docker Compose + install.sh)
```

## Geliştirme

Gereken: .NET 10 SDK. Tailwind ilk derlemede `.tools/` altına kendiliğinden indirilir, Node.js gerekmez.

**Aspire ile (Docker gerekir):**

```bash
dotnet run --project src/Arma.AppHost
```

PostgreSQL konteynerini ve uygulamayı birlikte başlatır, şemayı günceller ve Aspire panelini açar.

**Kendi PostgreSQL'inizle:** `src/Arma.Host/appsettings.Development.json` içindeki bağlantıyı düzenleyin, sonra:

```bash
dotnet run --project src/Arma.Host
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

## Mimari kurallar

- Her modül kendi PostgreSQL şemasını kullanır; başka modülün tablosuna yazmaz.
- Modüller birbirine yalnızca `*.Contracts` projeleri üzerinden erişir.
- Her tabloda `tenant_id` vardır. EF sorgu filtresine ek olarak PostgreSQL Row Level Security
  politikası, `app.tenant_id` oturum değişkenine göre başka kurumun satırını gizler.
- Şema değişiklikleri uygulama açılışında değil `dotnet Arma.Host.dll migrate` komutuyla uygulanır
  (geliştirmede `Arma:MigrateOnStartup` ile otomatik).
- Arayüz kodu yalnızca `Arma.UI` bileşenlerini kullanır; sayfalarda yerleşim dışında ham Tailwind sınıfı
  kullanılmaz.

## Sıradaki adımlar (Faz 0'dan kalanlar)

- Kimlik ve yetki: ASP.NET Core Identity + OpenIddict, 2FA. **Şu an API ve sayfalarda kimlik doğrulaması yok.**
- `Arma.UI`: Dialog, Select, Combobox, Dropdown Menu, Sheet, Tabs, Toast, Data Table.
- Wolverine ile modüller arası olaylar ve outbox.
- Windows Server (IIS) kurulum paketi.
