#!/bin/sh
# Восстановление HMS из бэкапа hms-*.tar.age. Нужен приватный ключ age (файл AGE_IDENTITY_FILE,
# по умолчанию /run/age-identity.txt) — его кладут на сервер только на время восстановления.
#   check <файл>          восстановить во временную БД hms_restore_check, сверить с манифестом, удалить
#   apply <файл> --yes    заменить рабочую БД и файлы (API остановлен; перед этим — страховочный бэкап)
set -eu
# shellcheck disable=SC3040 # pipefail есть в BusyBox ash (образ Alpine)
set -o pipefail
umask 077
. /usr/local/lib/hms-backup/common.sh

mode=${1:?Режим: check | apply}
file=${2:?Укажите файл бэкапа (имя из «hms-backup list» или путь)}
confirm=${3:-}
dir=${BACKUP_DIR:-/backups}
files=${FILES_DIR:-/files}
identity=${AGE_IDENTITY_FILE:-/run/age-identity.txt}
check_db=hms_restore_check

require_db_env
[ -f "$file" ] || file="$dir/$file"
[ -f "$file" ] || fail "файл бэкапа не найден: $file"
[ -f "$identity" ] || fail "нет приватного ключа age: $identity (смонтируйте файл ключа, см. deployment.md)"

if [ -f "$file.sha256" ]; then
    (cd "$(dirname "$file")" && sha256sum -c "$(basename "$file").sha256" >/dev/null) || fail "контрольная сумма не совпадает: $file"
    log "контрольная сумма верна"
fi

work=$(mktemp -d /tmp/hms-restore.XXXXXX)
trap 'rm -rf "$work"' EXIT

age -d -i "$identity" "$file" | tar -C "$work" -xf -
[ -s "$work/db.dump" ] && [ -f "$work/manifest.txt" ] && [ -f "$work/files.tar" ] || fail "архив неполный: нет db.dump, files.tar или manifest.txt"
log "бэкап расшифрован: $(grep '^created_at=' "$work/manifest.txt")"

# Ожидаемая сводка БД из манифеста (строки, которые выдаёт db_summary).
grep -vE '^(created_at|database|pg_dump|files)=' "$work/manifest.txt" > "$work/expected.txt"
expected_files=$(grep '^files=' "$work/manifest.txt" | cut -d= -f2)
actual_files=$(tar -tf "$work/files.tar" | grep -cv '/$' || true)
[ "$expected_files" = "$actual_files" ] || fail "файлов в архиве $actual_files, в манифесте $expected_files"

recreate_db() {
    psql -X -v ON_ERROR_STOP=1 -d postgres -q -v db="$1" <<'SQL'
DROP DATABASE IF EXISTS :"db" WITH (FORCE);
CREATE DATABASE :"db";
SQL
}

# Вызывается внутри if, где set -e не действует, — каждая ошибка проверяется явно.
restore_into() {
    recreate_db "$1" || return 1
    pg_restore --no-owner --no-privileges --exit-on-error --dbname="$1" "$work/db.dump" || return 1
    db_summary "$1" > "$work/restored.txt" || return 1
    if ! diff "$work/expected.txt" "$work/restored.txt" > "$work/diff.txt"; then
        cat "$work/diff.txt" >&2
        return 1
    fi
}

case "$mode" in
    check)
        # Временная БД удаляется в любом случае: проверка не оставляет копий ПД на диске.
        if restore_into "$check_db"; then ok=1; else ok=0; fi
        psql -X -d postgres -q -c "DROP DATABASE IF EXISTS $check_db WITH (FORCE)"
        [ "$ok" = 1 ] || fail "восстановленная БД не совпадает с манифестом (см. различия выше)"
        log "ПРОВЕРКА ПРОЙДЕНА: БД восстановлена и совпадает с манифестом, файлов: $actual_files"
        sed 's/^/  /' "$work/restored.txt" >&2
        ;;
    apply)
        [ "$confirm" = "--yes" ] || fail "восстановление ЗАМЕНИТ рабочие данные. Повторите с --yes"
        others=$(psql -X -d postgres -Atq -v db="$PGDATABASE" <<'SQL'
SELECT count(*) FROM pg_stat_activity WHERE datname = :'db' AND pid <> pg_backend_pid();
SQL
)
        [ "$others" = "0" ] || fail "к БД $PGDATABASE есть подключения ($others): остановите api — docker compose stop api"

        log "страховочный бэкап текущего состояния перед заменой"
        /usr/local/lib/hms-backup/backup.sh

        restore_into "$PGDATABASE" || fail "БД восстановлена, но не совпадает с манифестом — проверьте вручную; страховочный бэкап сохранён"

        # Владелец каталога файлов (пользователь API) — тот же, что у корня volume.
        owner=$(stat -c '%u:%g' "$files")
        find "$files" -mindepth 1 -delete
        tar -C "$files" -xf "$work/files.tar"
        chown -R "$owner" "$files"
        log "ГОТОВО: БД и файлы восстановлены. Дальше — migrate (права роли приложения) и запуск api/web; ./deploy/prod.sh restore-apply делает это сам"
        ;;
    *)
        fail "неизвестный режим: $mode (check | apply)"
        ;;
esac
