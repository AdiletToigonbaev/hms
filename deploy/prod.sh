#!/usr/bin/env bash
# Управление production-стеком HMS на сервере. Запуск из корня репозитория: ./deploy/prod.sh <команда>
# Подробно: docs/development/deployment.md.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
env_file=${HMS_ENV_FILE:-$root/deploy/.env.production}
compose=(docker compose -f "$root/deploy/docker-compose.prod.yml")
# Только для локальной проверки и CI: дополнительный файл compose (deploy/docker-compose.prod-local.yml).
if [ -n "${HMS_COMPOSE_EXTRA:-}" ]; then
    compose+=(-f "$HMS_COMPOSE_EXTRA")
fi
compose+=(--env-file "$env_file")

usage() {
    cat <<'EOF'
Команды:
  check           проверить .env.production и конфигурацию compose
  release         собрать образы, применить миграции, перезапустить сервисы (обычный релиз)
  migrate         только миграции + сидеры + права роли приложения
  up | down       запустить | остановить все сервисы (данные сохраняются)
  status          состояние контейнеров и /health/ready API
  logs [сервис]   журналы (Ctrl+C — выход)
  cert-init       первый выпуск сертификата Let's Encrypt (DNS уже указывает на сервер)
  backup          бэкап сейчас
  backups         список бэкапов на сервере
  restore-check <файл>   проверка восстановления во временную БД (раз в месяц)
  restore-apply <файл>   ЗАМЕНИТЬ данные содержимым бэкапа (с подтверждением)
EOF
}

# Значение переменной: из окружения оболочки (у docker compose оно приоритетнее), иначе из env-файла.
env_value() {
    if [ -n "${!1:-}" ]; then
        printf '%s' "${!1}"
    else
        grep -E "^$1=" "$env_file" | tail -n1 | cut -d= -f2- || true
    fi
}

require_env() {
    [ -f "$env_file" ] || { echo "Нет $env_file — скопируйте deploy/production.env.example и заполните" >&2; exit 1; }
    local perms
    perms=$(stat -c '%a' "$env_file" 2>/dev/null || echo 600)
    if [ "$perms" != "600" ] && [ "$perms" != "400" ]; then
        echo "Внимание: права $env_file = $perms; рекомендуется chmod 600 (в файле секреты)" >&2
    fi
    local missing=()
    for key in DOMAIN LETSENCRYPT_EMAIL POSTGRES_USER POSTGRES_PASSWORD APP_DB_PASSWORD JWT_SIGNING_KEY \
               SMTP_HOST SMTP_FROM_ADDRESS BACKUP_AGE_RECIPIENT; do
        [ -n "$(env_value "$key")" ] || missing+=("$key")
    done
    if [ ${#missing[@]} -gt 0 ]; then
        echo "Не заданы в $env_file: ${missing[*]}" >&2
        exit 1
    fi
    if [[ "$(env_value DOMAIN) $(env_value DOMAIN_ALIASES)" == *example.com* ]]; then
        echo "DOMAIN или DOMAIN_ALIASES — значение из примера; укажите домен гостиницы" >&2
        exit 1
    fi
}

api_ready() {
    "${compose[@]}" exec -T web node -e \
        "fetch('http://api:8080/health/ready').then(async r=>{console.log(r.status, await r.text());process.exit(r.ok?0:1)}).catch(e=>{console.error(e.message);process.exit(1)})"
}

cmd=${1:-help}
[ $# -gt 0 ] && shift

case "$cmd" in
    check)
        require_env
        "${compose[@]}" config --quiet && echo "Конфигурация в порядке"
        ;;
    release)
        require_env
        "${compose[@]}" build --pull
        "${compose[@]}" up -d db
        "${compose[@]}" run --rm migrate
        "${compose[@]}" up -d --remove-orphans
        echo "Ожидание готовности API..."
        for _ in $(seq 1 30); do api_ready >/dev/null 2>&1 && break; sleep 2; done
        api_ready
        ;;
    migrate)
        require_env
        "${compose[@]}" up -d db
        "${compose[@]}" run --rm migrate
        ;;
    up)
        require_env
        "${compose[@]}" up -d --remove-orphans
        ;;
    down)
        "${compose[@]}" down
        ;;
    status)
        "${compose[@]}" ps
        api_ready || true
        ;;
    logs)
        "${compose[@]}" logs -f --tail=200 "$@"
        ;;
    cert-init)
        require_env
        domain=$(env_value DOMAIN)
        # Один сертификат на основной домен и дополнительные имена (www). --expand — если имя добавили позже.
        names=(-d "$domain")
        for alias in $(env_value DOMAIN_ALIASES); do
            names+=(-d "$alias")
        done
        "${compose[@]}" up -d nginx
        "${compose[@]}" run --rm --entrypoint certbot certbot certonly --webroot -w /var/www/certbot \
            --cert-name "$domain" "${names[@]}" --expand \
            --email "$(env_value LETSENCRYPT_EMAIL)" --agree-tos --no-eff-email --non-interactive
        "${compose[@]}" exec nginx sh -c 'hms-cert && nginx -s reload'
        echo "Сертификат выпущен; nginx перезагружен. Проверьте: https://$domain"
        ;;
    backup)
        require_env
        "${compose[@]}" exec backup hms-backup backup
        ;;
    backups)
        "${compose[@]}" exec backup hms-backup list
        ;;
    restore-check)
        require_env
        [ $# -ge 1 ] || { echo "Укажите файл: ./deploy/prod.sh backups" >&2; exit 2; }
        "${compose[@]}" run --rm restore restore-check "$1"
        ;;
    restore-apply)
        require_env
        [ $# -ge 1 ] || { echo "Укажите файл: ./deploy/prod.sh backups" >&2; exit 2; }
        echo "ВНИМАНИЕ: рабочая БД и файлы будут ЗАМЕНЕНЫ содержимым $1 (перед этим — страховочный бэкап)."
        read -r -p "Введите «восстановить» для продолжения: " answer
        [ "$answer" = "восстановить" ] || { echo "Отменено"; exit 1; }
        "${compose[@]}" stop api web
        "${compose[@]}" run --rm restore restore-apply "$1" --yes
        "${compose[@]}" run --rm migrate
        "${compose[@]}" up -d
        ;;
    help|-h|--help)
        usage
        ;;
    *)
        usage
        exit 2
        ;;
esac
