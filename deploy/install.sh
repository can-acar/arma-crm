#!/usr/bin/env bash
# Arma CRM'i bu sunucuya kurar ya da günceller. Docker ve Docker Compose gerekir.
set -euo pipefail
cd "$(dirname "$0")"

if [[ ! -f .env ]]; then
  password=$(openssl rand -base64 24 | tr -d '/+=')
  tenant=$(cat /proc/sys/kernel/random/uuid)
  sed -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=${password}|" \
      -e "s|^ARMA_TENANT_ID=.*|ARMA_TENANT_ID=${tenant}|" .env.example > .env
  chmod 600 .env
  echo "Yeni .env oluşturuldu. Bu dosyayı yedekleyin; veritabanı parolası içinde."
fi

docker compose build
docker compose up -d
echo "Arma CRM çalışıyor: http://localhost:$(grep '^ARMA_PORT=' .env | cut -d= -f2)"
