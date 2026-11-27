#!/bin/sh
# Выбирает сертификат для ${DOMAIN} и пишет /etc/nginx/hms-cert.conf; для дополнительных имён
# (${DOMAIN_ALIASES}, например www.) пишет редирект /etc/nginx/conf.d/hms-aliases.conf.
# Есть сертификат Let's Encrypt — он; нет (первый запуск, локальная проверка) — временный самоподписанный,
# чтобы nginx мог стартовать и отвечать на проверку Let's Encrypt по HTTP.
set -eu
: "${DOMAIN:?Не задан DOMAIN}"
aliases=${DOMAIN_ALIASES:-}

# Имена попадают в конфигурацию nginx — только буквы, цифры, точки и дефисы.
for name in $DOMAIN $aliases; do
    case "$name" in
        *[!a-zA-Z0-9.-]*|'') echo "hms-cert: недопустимое имя домена: $name" >&2; exit 1 ;;
    esac
done

le="/etc/letsencrypt/live/${DOMAIN}"
out=/etc/nginx/hms-cert.conf

if [ -s "${le}/fullchain.pem" ] && [ -s "${le}/privkey.pem" ]; then
    cert="${le}/fullchain.pem"
    key="${le}/privkey.pem"
else
    dir=/etc/nginx/selfsigned
    names="$DOMAIN $aliases"
    # Пересоздать, если изменился список имён (добавили www).
    if [ ! -s "${dir}/cert.pem" ] || [ "$(cat "${dir}/names" 2>/dev/null)" != "$names" ]; then
        mkdir -p "${dir}"
        san=""
        for name in $names; do
            san="${san:+$san,}DNS:${name}"
        done
        openssl req -x509 -nodes -newkey rsa:2048 -days 30 \
            -subj "/CN=${DOMAIN}" -addext "subjectAltName=${san}" \
            -keyout "${dir}/key.pem" -out "${dir}/cert.pem" 2>/dev/null
        printf '%s' "$names" > "${dir}/names"
    fi
    cert="${dir}/cert.pem"
    key="${dir}/key.pem"
    echo "hms-cert: сертификата Let's Encrypt для ${DOMAIN} нет — используется временный самоподписанный" >&2
fi

printf 'ssl_certificate %s;\nssl_certificate_key %s;\n' "${cert}" "${key}" > "${out}.tmp"
mv "${out}.tmp" "${out}"

# Редирект с дополнительных имён на основной домен (один адрес сайта — для SEO и cookie сессии).
alias_conf=/etc/nginx/conf.d/hms-aliases.conf
if [ -n "$aliases" ]; then
    cat > "${alias_conf}.tmp" <<EOF
# Сгенерировано hms-cert из DOMAIN_ALIASES — не редактировать.
server {
    listen 80;
    listen [::]:80;
    server_name ${aliases};
    location /.well-known/acme-challenge/ { root /var/www/certbot; }
    location / { return 301 https://${DOMAIN}\$request_uri; }
}

server {
    listen 443 ssl;
    listen [::]:443 ssl;
    http2 on;
    server_name ${aliases};
    include /etc/nginx/hms-cert.conf;
    add_header Strict-Transport-Security "max-age=31536000" always;
    return 301 https://${DOMAIN}\$request_uri;
}
EOF
else
    echo "# DOMAIN_ALIASES не задан — дополнительных имён нет." > "${alias_conf}.tmp"
fi
mv "${alias_conf}.tmp" "${alias_conf}"
