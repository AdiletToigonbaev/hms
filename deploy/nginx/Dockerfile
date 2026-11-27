# syntax=docker/dockerfile:1
# Nginx HMS: HTTPS, заголовки безопасности, лимиты, прокси на web (Next.js) и вебхуки API.
FROM nginx:1.30-alpine
# openssl — для временного самоподписанного сертификата до выпуска Let's Encrypt.
RUN apk add --no-cache openssl \
    && rm -f /etc/nginx/conf.d/default.conf \
    && mkdir -p /var/www/certbot
COPY nginx.conf /etc/nginx/nginx.conf
COPY templates/ /etc/nginx/templates/
COPY --chmod=755 hms-cert.sh /usr/local/bin/hms-cert
# После envsubst (20-…) выбираем сертификат и запускаем фоновую перезагрузку; затем стандартный запуск nginx.
# CMD не переопределяется: entrypoint образа выполняет /docker-entrypoint.d/ только для команды «nginx».
COPY --chmod=755 25-hms-cert.sh /docker-entrypoint.d/25-hms-cert.sh
