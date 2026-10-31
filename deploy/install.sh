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
  echo "Yeni .env oluşturuldu. Bu dosyayı yedekleyin; veritabanı parolası ve şifreleme anahtarı içinde."
fi

# Hassas alan şifreleme anahtarı: yeni kurulumda ve bu özellikten önce kurulmuş sistemlerde bir kez üretilir.
if ! grep -q '^ARMA_ENCRYPTION_KEY=.\+' .env; then
  key=$(openssl rand -base64 32)
  if grep -q '^ARMA_ENCRYPTION_KEY=' .env; then
    sed -i "s|^ARMA_ENCRYPTION_KEY=.*|ARMA_ENCRYPTION_KEY=${key}|" .env
  else
    printf '\nARMA_ENCRYPTION_KEY=%s\n' "$key" >> .env
  fi
  echo "Şifreleme anahtarı üretildi (ARMA_ENCRYPTION_KEY). .env dosyasını yeniden yedekleyin; anahtar kaybolursa kimlik numaraları okunamaz."
fi

docker compose build
docker compose up -d
echo "Arma CRM çalışıyor: http://localhost:$(grep '^ARMA_PORT=' .env | cut -d= -f2)"
echo "İlk açılışta tarayıcıda yönetici hesabı oluşturma ekranı gelir."
