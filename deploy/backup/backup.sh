#!/bin/sh
# Бэкап HMS: БД (pg_dump, формат custom) + загруженные файлы + манифест → один tar, зашифрованный age.
# Результат: ${BACKUP_DIR}/hms-ГГГГММДД-ЧЧММСС.tar.age (+ .sha256). Хранение — BACKUP_RETENTION_DAYS дней;
# если задан RCLONE_REMOTE — копия вне сервера (хранение BACKUP_REMOTE_RETENTION_DAYS дней).
set -eu
# Ошибка tar в конвейере «tar | age» не должна дать «успешный» битый бэкап.
# shellcheck disable=SC3040 # pipefail есть в BusyBox ash (образ Alpine)
set -o pipefail
umask 077
. /usr/local/lib/hms-backup/common.sh

dir=${BACKUP_DIR:-/backups}
files=${FILES_DIR:-/files}
keep=${BACKUP_RETENTION_DAYS:-14}
remote_keep=${BACKUP_REMOTE_RETENTION_DAYS:-30}
marker="$dir/.last-success"

if [ "${1:-}" = "--health" ]; then
    # Здоров, если успешный бэкап был не позже 26 часов назад (сутки + запас на долгий бэкап).
    # До первого бэкапа после запуска — здоров первые 26 часов.
    ref=$marker
    [ -f "$ref" ] || ref=/tmp/hms-backup-started
    [ -f "$ref" ] && [ -n "$(find "$ref" -mmin -1560)" ] && exit 0
    echo "Нет успешного бэкапа за 26 часов" >&2
    exit 1
fi

require_db_env
: "${BACKUP_AGE_RECIPIENT:?Не задан BACKUP_AGE_RECIPIENT — публичный ключ age (age1...). Бэкапы без шифрования не создаются.}"

mkdir -p "$dir"
ts=$(date +%Y%m%d-%H%M%S)
name="hms-$ts.tar.age"
work=$(mktemp -d /tmp/hms-backup.XXXXXX)
trap 'rm -rf "$work" "$dir/$name.part"' EXIT

log "начало: $name"

pg_dump --format=custom --compress=6 --no-owner --no-privileges --file="$work/db.dump"

if [ -d "$files" ]; then
    tar -C "$files" -cf "$work/files.tar" .
    file_count=$(tar -tf "$work/files.tar" | grep -cv '/$' || true)
else
    log "каталог файлов $files не найден — в бэкап попадает только БД"
    tar -cf "$work/files.tar" -T /dev/null
    file_count=0
fi

{
    echo "created_at=$(date -Iseconds)"
    echo "database=$PGDATABASE"
    echo "pg_dump=$(pg_dump --version)"
    echo "files=$file_count"
    db_summary "$PGDATABASE"
} > "$work/manifest.txt"

# Несколько получателей (ключ владельца + запасной) — через пробел.
recipients=""
for r in $BACKUP_AGE_RECIPIENT; do
    recipients="$recipients -r $r"
done

# shellcheck disable=SC2086 # recipients — намеренно разбивается на аргументы
tar -C "$work" -cf - manifest.txt db.dump files.tar | age $recipients -o "$dir/$name.part"
mv "$dir/$name.part" "$dir/$name"
(cd "$dir" && sha256sum "$name" > "$name.sha256")

size=$(du -h "$dir/$name" | cut -f1)
log "готово: $name ($size, файлов: $file_count)"

# Хранение на сервере.
find "$dir" -maxdepth 1 -name 'hms-*.tar.age*' -mtime +"$keep" -print -delete | while read -r old; do
    log "удалён старый бэкап: $(basename "$old")"
done

# Копия вне сервера: потеря VPS не должна означать потерю данных.
if [ -n "${RCLONE_REMOTE:-}" ]; then
    rclone copy --no-traverse "$dir/$name" "$RCLONE_REMOTE"
    rclone copy --no-traverse "$dir/$name.sha256" "$RCLONE_REMOTE"
    rclone delete --min-age "${remote_keep}d" --include 'hms-*.tar.age*' "$RCLONE_REMOTE"
    log "копия вне сервера: $RCLONE_REMOTE"
else
    log "RCLONE_REMOTE не задан — копия только на сервере (настройте хранилище вне VPS)"
fi

touch "$marker"
