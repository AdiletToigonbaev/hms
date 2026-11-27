#!/usr/bin/env bash
# НЕ ДЛЯ СЕРВЕРА. Проверка production-сборки на своём компьютере и в CI:
# тот же docker-compose.prod.yml (nginx + HTTPS с самоподписанным сертификатом, роль БД приложения, бэкап),
# но домен localhost и Mailpit вместо SMTP.
#   ./deploy/prod-local-check.sh up             собрать и запустить (https://localhost)
#   ./deploy/prod-local-check.sh backup-check   бэкап → проверка восстановления во временную БД
#   ./deploy/prod-local-check.sh down           остановить и удалить данные проверочного стека
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
export HMS_ENV_FILE="$root/deploy/prod-local.env"
export HMS_COMPOSE_EXTRA="$root/deploy/docker-compose.prod-local.yml"
prod="$root/deploy/prod.sh"

# Тестовый ключ age — только на этом компьютере (deploy/backups/ не в git).
mkdir -p "$root/deploy/backups"
key="$root/deploy/backups/test-age-identity.txt"
if [ ! -s "$key" ]; then
    # Ключа ещё нет, а compose требует BACKUP_AGE_RECIPIENT — для генерации подставляется заглушка.
    keygen=(env BACKUP_AGE_RECIPIENT=pending docker compose -f "$root/deploy/docker-compose.prod.yml"
            -f "$HMS_COMPOSE_EXTRA" --env-file "$HMS_ENV_FILE")
    "${keygen[@]}" build backup >/dev/null
    "${keygen[@]}" run --rm -T --no-deps --entrypoint age-keygen backup > "$key" 2>/dev/null
fi
BACKUP_AGE_RECIPIENT=$(grep -m1 '^# public key:' "$key" | awk '{print $4}')
export BACKUP_AGE_RECIPIENT

case "${1:-up}" in
    up)
        "$prod" release
        echo "Готово: https://localhost (сертификат самоподписанный), письма — http://localhost:8025"
        echo "E2E: cd frontend && E2E_BASE_URL=https://localhost E2E_IGNORE_HTTPS_ERRORS=1 E2E_OWNER_INITIAL_PASSWORD=owner-prod-local-password npm run test:e2e"
        ;;
    backup-check)
        "$prod" backup
        latest=$(ls -1t "$root/deploy/backups"/hms-*.tar.age | head -n1)
        "$prod" restore-check "$(basename "$latest")"
        ;;
    down)
        docker compose -f "$root/deploy/docker-compose.prod.yml" -f "$HMS_COMPOSE_EXTRA" --env-file "$HMS_ENV_FILE" \
            --profile tools down -v
        ;;
    *)
        echo "Команды: up | backup-check | down" >&2
        exit 2
        ;;
esac
