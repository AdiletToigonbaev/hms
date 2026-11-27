#!/bin/sh
# Шаг entrypoint образа nginx (после envsubst шаблонов): выбрать сертификат до запуска nginx
# и запустить фоновую перезагрузку — раз в 6 часов перечитать сертификат (продление certbot,
# первый выпуск) и мягко перезагрузить nginx без разрыва соединений.
set -eu
/usr/local/bin/hms-cert

(
    while :; do
        sleep 21600
        /usr/local/bin/hms-cert && nginx -s reload
    done
) </dev/null >/dev/null 2>&1 &
