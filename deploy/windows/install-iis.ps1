<#
.SYNOPSIS
    Arma CRM'i bu Windows sunucusuna IIS üzerinde kurar ya da günceller.

.DESCRIPTION
    Paketin kök klasöründen yönetici olarak çalıştırın:

        powershell -ExecutionPolicy Bypass -File .\install-iis.ps1 -CreateDatabase

    Betik sırasıyla:
      1. IIS, WebSocket Protokolü ve Uygulama Başlatma özelliklerini açar.
      2. ASP.NET Core Hosting Bundle'ın (.NET 10) kurulu olduğunu denetler.
      3. İlk kurulumda C:\ProgramData\Arma\arma.env dosyasını üretir (kurum kimliği; -CreateDatabase ile veritabanı ve parola).
      4. Uygulama dosyalarını site klasörüne kopyalar (güncellemede site app_offline.htm ile kısa süre kapatılır).
      5. "migrate" komutuyla veritabanı şemasını günceller.
      6. Uygulama havuzunu (No Managed Code, sürekli açık) ve siteyi oluşturur, ayarları havuzun ortam değişkenlerine yazar.

    Güncellemede yeni paketi açıp aynı komutu (-CreateDatabase olmadan) çalıştırmak yeterlidir; arma.env korunur.

.PARAMETER SiteName
    IIS site ve uygulama havuzu adı.

.PARAMETER Port
    Sitenin dinleyeceği HTTP portu. HTTPS bağlamasını IIS Yöneticisi'nden sertifikanızla ekleyin.

.PARAMETER HostName
    Bağlamanın ana bilgisayar adı (crm.kurum.org). Boşsa tüm adlara yanıt verir.

.PARAMETER InstallPath
    Uygulama dosyalarının kopyalanacağı klasör.

.PARAMETER CreateDatabase
    Yerel PostgreSQL'de "arma" rolünü ve veritabanını oluşturur, parolayı üretip arma.env'e yazar.
    postgres süper kullanıcısının parolası sorulur; yalnızca bu işlem için kullanılır, hiçbir yere kaydedilmez.

.PARAMETER PostgresBin
    psql.exe'nin bulunduğu klasör. Boşsa C:\Program Files\PostgreSQL altındaki en yeni sürüm kullanılır.
#>
[CmdletBinding()]
param(
    [string]$SiteName = "ArmaCRM",
    [int]$Port = 8080,
    [string]$HostName = "",
    [string]$InstallPath = "C:\inetpub\arma-crm",
    [switch]$CreateDatabase,
    [string]$PostgresHost = "localhost",
    [int]$PostgresPort = 5432,
    [string]$PostgresBin = ""
)

$ErrorActionPreference = "Stop"
$PackageRoot = $PSScriptRoot
$ConfigDir = Join-Path $env:ProgramData "Arma"
$EnvFile = Join-Path $ConfigDir "arma.env"

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

function Assert-Administrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Bu betik yönetici olarak çalıştırılmalı (PowerShell'i 'Yönetici olarak çalıştır' ile açın)."
    }
}

function Enable-IisFeatures {
    Write-Step "IIS özellikleri denetleniyor"
    $isServer = (Get-CimInstance Win32_OperatingSystem).ProductType -ne 1
    if ($isServer) {
        $features = "Web-Server", "Web-WebSockets", "Web-AppInit", "Web-Mgmt-Console"
        $missing = $features | Where-Object { -not (Get-WindowsFeature $_).Installed }
        if ($missing) { Install-WindowsFeature $missing | Out-Null }
    }
    else {
        $features = "IIS-WebServerRole", "IIS-WebServer", "IIS-WebSockets", "IIS-ApplicationInit", "IIS-ManagementConsole"
        foreach ($feature in $features) {
            if ((Get-WindowsOptionalFeature -Online -FeatureName $feature).State -ne "Enabled") {
                Enable-WindowsOptionalFeature -Online -FeatureName $feature -All -NoRestart | Out-Null
            }
        }
    }
}

function Assert-HostingBundle {
    Write-Step "ASP.NET Core Hosting Bundle denetleniyor"
    $module = Join-Path $env:ProgramFiles "IIS\Asp.Net Core Module\V2\aspnetcorev2.dll"
    $runtimes = & dotnet --list-runtimes 2>$null
    if (-not (Test-Path $module) -or -not ($runtimes -match "Microsoft\.AspNetCore\.App 10\.")) {
        throw "ASP.NET Core 10 Hosting Bundle kurulu değil. https://dotnet.microsoft.com/download/dotnet/10.0 adresinden 'Hosting Bundle'ı kurun, sonra bu betiği yeniden çalıştırın."
    }
}

function Read-EnvFile([string]$Path) {
    $values = [ordered]@{}
    foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) { continue }
        $index = $trimmed.IndexOf("=")
        if ($index -lt 1) { continue }
        $values[$trimmed.Substring(0, $index).Trim()] = $trimmed.Substring($index + 1).Trim()
    }
    return $values
}

function Set-EnvValue([string]$Path, [string]$Key, [string]$Value) {
    $lines = Get-Content -LiteralPath $Path -Encoding UTF8
    $pattern = "^\s*" + [regex]::Escape($Key) + "="
    $lines = $lines | ForEach-Object { if ($_ -match $pattern) { "$Key=$Value" } else { $_ } }
    Set-Content -LiteralPath $Path -Value $lines -Encoding UTF8
}

function New-Secret([int]$Length = 32) {
    $chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789".ToCharArray()
    $bytes = New-Object byte[] $Length
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

function Protect-ConfigDir {
    # Yalnızca yöneticiler ve SYSTEM okuyabilir; dosyada veritabanı parolası ve anahtarlar var.
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($identity in "BUILTIN\Administrators", "NT AUTHORITY\SYSTEM") {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")))
    }
    Set-Acl -LiteralPath $ConfigDir -AclObject $acl
}

function Initialize-EnvFile {
    if (Test-Path $EnvFile) {
        Write-Step "Mevcut ayarlar kullanılıyor: $EnvFile"
        Initialize-EncryptionKey
        return
    }

    Write-Step "Ayar dosyası oluşturuluyor: $EnvFile"
    New-Item -ItemType Directory -Force -Path $ConfigDir | Out-Null
    Protect-ConfigDir
    Copy-Item -LiteralPath (Join-Path $PackageRoot "arma.env.example") -Destination $EnvFile
    Set-EnvValue $EnvFile "Arma__TenantId" ([guid]::NewGuid().ToString())
    Initialize-EncryptionKey
    Write-Host "Bu dosyayı yedekleyin; kurum kimliği, veritabanı parolası ve şifreleme anahtarı içinde." -ForegroundColor Yellow
}

# Hassas alan şifreleme anahtarı; bu özellikten önce kurulmuş sistemlerde de bir kez üretilir.
function Initialize-EncryptionKey {
    $settings = Read-EnvFile $EnvFile
    if ($settings["Arma__Security__EncryptionKey"]) {
        return
    }

    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $key = [Convert]::ToBase64String($bytes)
    if ((Get-Content -LiteralPath $EnvFile -Encoding UTF8) -match "^\s*Arma__Security__EncryptionKey=") {
        Set-EnvValue $EnvFile "Arma__Security__EncryptionKey" $key
    } else {
        Add-Content -LiteralPath $EnvFile -Value "Arma__Security__EncryptionKey=$key" -Encoding UTF8
    }
    Write-Host "Şifreleme anahtarı üretildi. $EnvFile dosyasını yeniden yedekleyin; anahtar kaybolursa kimlik numaraları okunamaz." -ForegroundColor Yellow
}

function Find-Psql {
    if ($PostgresBin) { return Join-Path $PostgresBin "psql.exe" }
    $root = Join-Path $env:ProgramFiles "PostgreSQL"
    $candidate = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [int]($_.Name -replace "\D", "0") } -Descending |
        ForEach-Object { Join-Path $_.FullName "bin\psql.exe" } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $candidate) { throw "psql.exe bulunamadı. PostgreSQL 18'i kurun ya da -PostgresBin ile bin klasörünü verin." }
    return $candidate
}

function Initialize-Database {
    $current = (Read-EnvFile $EnvFile)["ConnectionStrings__arma"]
    if ($current -and $current -notmatch "Password=\s*(;|$)") {
        Write-Step "Veritabanı bağlantısı zaten tanımlı; oluşturma atlandı"
        return
    }

    Write-Step "PostgreSQL'de arma rolü ve veritabanı oluşturuluyor"
    $psql = Find-Psql
    $superPassword = Read-Host "postgres kullanıcısının parolası" -AsSecureString
    $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($superPassword))
    try {
        $password = New-Secret
        # Uygulama rolü süper kullanıcı değildir; satır düzeyi güvenlik (RLS) yalnızca böyle uygulanır.
        $sql = @"
DO `$`$ BEGIN
  IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'arma') THEN
    ALTER ROLE arma WITH LOGIN NOSUPERUSER PASSWORD '$password';
  ELSE
    CREATE ROLE arma WITH LOGIN NOSUPERUSER PASSWORD '$password';
  END IF;
END `$`$;
"@
        & $psql -h $PostgresHost -p $PostgresPort -U postgres -d postgres -v ON_ERROR_STOP=1 -q -c $sql
        if ($LASTEXITCODE -ne 0) { throw "arma rolü oluşturulamadı." }
        $exists = & $psql -h $PostgresHost -p $PostgresPort -U postgres -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'arma'"
        if ($exists -ne "1") {
            & $psql -h $PostgresHost -p $PostgresPort -U postgres -d postgres -v ON_ERROR_STOP=1 -q -c "CREATE DATABASE arma OWNER arma ENCODING 'UTF8' TEMPLATE template0"
            if ($LASTEXITCODE -ne 0) { throw "arma veritabanı oluşturulamadı." }
        }
    }
    finally {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }

    Set-EnvValue $EnvFile "ConnectionStrings__arma" "Host=$PostgresHost;Port=$PostgresPort;Database=arma;Username=arma;Password=$password"
}

function Copy-Application {
    Write-Step "Uygulama dosyaları kopyalanıyor: $InstallPath"
    $source = Join-Path $PackageRoot "app"
    if (-not (Test-Path (Join-Path $source "Arma.Host.dll"))) { throw "Paket eksik: $source\Arma.Host.dll bulunamadı." }

    New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null
    $offline = Join-Path $InstallPath "app_offline.htm"
    $updating = Test-Path (Join-Path $InstallPath "Arma.Host.dll")
    if ($updating) {
        # ASP.NET Core Modülü bu dosyayı görünce uygulamayı durdurur ve ziyaretçilere bu sayfayı gösterir.
        Set-Content -LiteralPath $offline -Encoding UTF8 -Value '<!doctype html><html lang="tr"><meta charset="utf-8"><title>Güncelleniyor</title><body style="font-family:sans-serif;padding:3rem"><h1>Arma CRM güncelleniyor</h1><p>Birkaç dakika içinde tekrar deneyin.</p></body></html>'
        Start-Sleep -Seconds 3
    }

    # /MIR eski sürümden kalan dosyaları siler; günlükler, eklentiler (plugins) ve bakım sayfası korunur.
    robocopy $source $InstallPath /MIR /XD logs plugins /XF app_offline.htm /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Dosyalar kopyalanamadı (robocopy çıkış kodu $LASTEXITCODE)." }
    New-Item -ItemType Directory -Force -Path (Join-Path $InstallPath "logs") | Out-Null
    return $offline
}

function Invoke-Migrations([hashtable]$Settings) {
    Write-Step "Veritabanı şeması güncelleniyor"
    $saved = @{}
    foreach ($key in $Settings.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
        [Environment]::SetEnvironmentVariable($key, $Settings[$key], "Process")
    }
    try {
        Push-Location $InstallPath
        & dotnet .\Arma.Host.dll migrate
        if ($LASTEXITCODE -ne 0) { throw "Şema güncellemesi başarısız oldu; yukarıdaki hatayı ve $EnvFile içindeki bağlantı bilgisini kontrol edin." }
    }
    finally {
        Pop-Location
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], "Process") }
    }
}

function Set-IisSite([hashtable]$Settings) {
    Write-Step "IIS uygulama havuzu ve site ayarlanıyor"
    Import-Module WebAdministration
    $poolPath = "IIS:\AppPools\$SiteName"
    if (-not (Test-Path $poolPath)) { New-WebAppPool -Name $SiteName | Out-Null }

    # .NET CLR gerekmez; zamanlanmış işler (hatırlatmalar, İYS) için havuz hiç kapanmaz ve geri dönüştürülmez.
    Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ""
    Set-ItemProperty $poolPath -Name startMode -Value "AlwaysRunning"
    Set-ItemProperty $poolPath -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
    Set-ItemProperty $poolPath -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)

    # Ayarlar applicationHost.config'te havuzun ortam değişkenleri olarak tutulur; site klasöründen okunamaz.
    $filter = "system.applicationHost/applicationPools/add[@name='$SiteName']/environmentVariables"
    Clear-WebConfiguration -PSPath "MACHINE/WEBROOT/APPHOST" -Filter $filter -ErrorAction SilentlyContinue
    foreach ($key in $Settings.Keys) {
        Add-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter $filter -Name "." -Value @{ name = $key; value = $Settings[$key] }
    }

    if (-not (Get-Website -Name $SiteName)) {
        New-Website -Name $SiteName -PhysicalPath $InstallPath -ApplicationPool $SiteName -Port $Port -HostHeader $HostName | Out-Null
    }
    else {
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $InstallPath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $SiteName
    }
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationDefaults.preloadEnabled -Value $true

    # Havuz kimliği uygulama klasörünü okur, günlük klasörüne yazar.
    $identity = "IIS AppPool\$SiteName"
    icacls $InstallPath /grant "${identity}:(OI)(CI)RX" /T /Q | Out-Null
    icacls (Join-Path $InstallPath "logs") /grant "${identity}:(OI)(CI)M" /T /Q | Out-Null

    Restart-WebAppPool -Name $SiteName -ErrorAction SilentlyContinue
    Start-Website -Name $SiteName -ErrorAction SilentlyContinue
}

Assert-Administrator
Enable-IisFeatures
Assert-HostingBundle
Initialize-EnvFile
if ($CreateDatabase) { Initialize-Database }

$settings = @{}
(Read-EnvFile $EnvFile).GetEnumerator() | Where-Object { $_.Value -ne "" } | ForEach-Object { $settings[$_.Key] = $_.Value }
if (-not $settings["ConnectionStrings__arma"] -or $settings["ConnectionStrings__arma"] -match "Password=\s*(;|$)") {
    throw "Veritabanı bağlantısı eksik. $EnvFile içinde ConnectionStrings__arma satırını doldurun ya da betiği -CreateDatabase ile çalıştırın."
}
$settings["ASPNETCORE_ENVIRONMENT"] = "Production"

$offline = Copy-Application
Invoke-Migrations $settings
Set-IisSite $settings
Remove-Item -LiteralPath $offline -ErrorAction SilentlyContinue

if ((Get-TimeZone).Id -ne "Turkey Standard Time") {
    Write-Host "Uyarı: Sunucunun saat dilimi $((Get-TimeZone).Id). Tarih ve saatler bu dilime göre gösterilir; gerekirse 'Set-TimeZone -Id ""Turkey Standard Time""' çalıştırın." -ForegroundColor Yellow
}

$address = if ($HostName) { "http://${HostName}:$Port" } else { "http://localhost:$Port" }
Write-Host ""
Write-Host "Arma CRM çalışıyor: $address" -ForegroundColor Green
Write-Host "İlk açılışta tarayıcıda yönetici hesabı oluşturma ekranı gelir. Canlı kullanımda IIS Yöneticisi'nden HTTPS bağlaması ekleyin."
