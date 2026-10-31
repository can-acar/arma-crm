#!/usr/bin/env bash
# Windows/IIS kurulum paketini üretir: deploy/out/arma-crm-iis-<sürüm>.zip
# Kullanım: deploy/windows/build-package.sh [sürüm]   (Linux, macOS ya da Windows'ta Git Bash; .NET 10 SDK gerekir)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
version="${1:-$(git -C "$root" describe --tags --always 2>/dev/null || date +%Y%m%d)}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

package="$work/arma-crm-iis-$version"
mkdir -p "$package/app"
dotnet publish "$root/src/Arma.Host/Arma.Host.csproj" -c Release -r win-x64 --self-contained false -o "$package/app" --nologo -v quiet
cp "$here/web.config" "$package/app/web.config"
cp "$here/install-iis.ps1" "$here/arma.env.example" "$here/KURULUM.txt" "$package/"
# appsettings.Development.json üretim paketine girmez.
rm -f "$package/app/appsettings.Development.json"

mkdir -p "$root/deploy/out"
output="$root/deploy/out/arma-crm-iis-$version.zip"
rm -f "$output"
(cd "$work" && zip -qr "$output" "arma-crm-iis-$version")
echo "$output"
