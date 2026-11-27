#!/bin/sh
# Общие функции скриптов бэкапа. Подключение к БД — стандартные PGHOST/PGUSER/PGPASSWORD/PGDATABASE
# (владелец схемы: pg_dump читает все таблицы, восстановление создаёт объекты).

log() {
    printf '%s hms-backup: %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*" >&2
}

fail() {
    log "ОШИБКА: $*"
    exit 1
}

require_db_env() {
    : "${PGHOST:?Не задан PGHOST}"
    : "${PGUSER:?Не задан PGUSER}"
    : "${PGPASSWORD:?Не задан PGPASSWORD}"
    : "${PGDATABASE:?Не задан PGDATABASE}"
}

# Сводка для сверки после восстановления: число таблиц, последняя миграция и строки ключевых таблиц.
# Одинаковый вывод на рабочей БД (в манифест бэкапа) и на восстановленной.
db_summary() {
    psql -X -v ON_ERROR_STOP=1 -d "$1" -Atq <<'SQL'
SELECT 'tables=' || count(*) FROM information_schema.tables
 WHERE table_schema NOT IN ('pg_catalog', 'information_schema') AND table_type = 'BASE TABLE';
SELECT 'last_migration=' || coalesce(max(migration_id), '-') FROM platform.__ef_migrations_history;
SELECT 'identity.users=' || count(*) FROM identity.users;
SELECT 'guests.guests=' || count(*) FROM guests.guests;
SELECT 'rooms.rooms=' || count(*) FROM rooms.rooms;
SELECT 'bookings.bookings=' || count(*) FROM bookings.bookings;
SELECT 'finance.payments=' || count(*) FROM finance.payments;
SELECT 'audit.audit_logs=' || count(*) FROM audit.audit_logs;
SELECT 'platform.stored_files=' || count(*) FROM platform.stored_files;
SQL
}
