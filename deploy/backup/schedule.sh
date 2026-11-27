#!/bin/sh
# Ежедневный бэкап в BACKUP_TIME (ЧЧ:ММ, часовой пояс контейнера — Asia/Bishkek).
# Без cron: цикл проверяет время раз в 20 секунд и запускает бэкап один раз в сутки.
set -eu
. /usr/local/lib/hms-backup/common.sh

at=${BACKUP_TIME:-03:30}
case "$at" in
    [0-2][0-9]:[0-5][0-9]) ;;
    *) fail "BACKUP_TIME должен быть в формате ЧЧ:ММ (сейчас: $at)" ;;
esac

touch /tmp/hms-backup-started
log "расписание: ежедневно в $at ($(date +%Z))"
last=""
while :; do
    today=$(date +%Y-%m-%d)
    if [ "$(date +%H:%M)" = "$at" ] && [ "$last" != "$today" ]; then
        last=$today
        /usr/local/lib/hms-backup/backup.sh || log "бэкап не выполнен — повтор завтра; healthcheck покажет unhealthy"
    fi
    sleep 20
done
