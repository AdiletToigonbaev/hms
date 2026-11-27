# Развёртывание HMS на сервере (production)

Sprint 11. Один VPS на Ubuntu, всё в Docker Compose (`deploy/docker-compose.prod.yml`), управление — `deploy/prod.sh`.
Архитектура окружения — `architecture/system-architecture.md` §7, меры безопасности — `architecture/security-architecture.md`.

## 1. Схема

```
Интернет ──80/443──▶ nginx ──▶ web (Next.js)  ──▶ api (ASP.NET Core) ──▶ db (PostgreSQL 18)
                       │                                ▲                      ▲
                       └── /api/v1/webhooks/ ───────────┘                      │
             certbot (Let's Encrypt) · backup (pg_dump + файлы → age → rclone) ┘
```

- Снаружи открыты только 80 и 443 (nginx). API, Next.js и БД — только во внутренней сети Docker.
- Браузер ходит в API только через Next.js (BFF). Снаружи к API пропускаются только вебхуки `/api/v1/webhooks/` (Meta, позже банки); остальное `/api/v1/*` — 404.
- API работает под ролью БД `hms_app` без DDL и без права менять журнал аудита; миграции и бэкап — под владельцем схемы (`POSTGRES_USER`).

| Сервис | Назначение | Запуск |
|---|---|---|
| `nginx` | HTTPS, заголовки безопасности, лимиты, прокси | всегда |
| `web` | сайт и кабинеты (Next.js standalone) | всегда |
| `api` | REST API, фоновые задачи (outbox, напоминания, истечение броней) | всегда |
| `db` | PostgreSQL 18 | всегда |
| `certbot` | продление сертификата (2 раза в сутки) | всегда |
| `backup` | ежедневный зашифрованный бэкап | всегда |
| `migrate` | миграции + сидеры + права роли приложения | вручную при релизе |
| `restore` | проверка и восстановление бэкапа | вручную |

## 2. Сервер

**Минимум:** 2 vCPU, 4 ГБ RAM, 40 ГБ SSD, Ubuntu 24.04 LTS (подходит и 22.04 LTS — Docker Engine её поддерживает; позже обновляется `do-release-upgrade`). Виртуализация — KVM: на OpenVZ/LXC Docker может не работать. Сборка образов идёт на сервере (Next.js — самый тяжёлый шаг, ~2 ГБ RAM).

### 2.1. Подготовка (один раз, под root или через sudo)

```bash
# Обновления и автоматические обновления безопасности
apt update && apt -y upgrade
apt -y install unattended-upgrades git ufw fail2ban
dpkg-reconfigure -plow unattended-upgrades

# Пользователь для администрирования (вход по SSH-ключу)
adduser hms && usermod -aG sudo hms
mkdir -p /home/hms/.ssh && cp ~/.ssh/authorized_keys /home/hms/.ssh/ && chown -R hms:hms /home/hms/.ssh

# SSH: только ключи, без входа root (проверьте вход под hms в новом окне, прежде чем закрыть текущее)
sed -i 's/^#\?PasswordAuthentication .*/PasswordAuthentication no/; s/^#\?PermitRootLogin .*/PermitRootLogin no/' /etc/ssh/sshd_config
systemctl reload ssh

# Межсетевой экран: SSH, HTTP, HTTPS
ufw allow OpenSSH && ufw allow 80/tcp && ufw allow 443/tcp && ufw enable

# Docker Engine + Compose plugin — по официальной инструкции docs.docker.com/engine/install/ubuntu/
usermod -aG docker hms

# Каталог бэкапов
mkdir -p /var/backups/hms && chmod 700 /var/backups/hms
```

> Docker публикует порты в обход ufw. Поэтому в `docker-compose.prod.yml` наружу опубликованы только 80/443 (nginx); у БД, API и web портов на хосте нет.

### 2.2. Домен

У регистратора (раздел DNS) — A-записи на IP сервера для самого домена (`@`) и для `www` (и AAAA, если у VPS есть IPv6). NS-серверы регистратора не менять. При DNS через Cloudflare — режим «DNS only» (серое облако): через прокси Cloudflare не пройдёт выпуск сертификата HTTP-01, а API увидит IP Cloudflare вместо гостей (лимиты входа).

Проверка: `dig +short hotel.example.com` и `dig +short www.hotel.example.com` — оба показывают IP сервера.

## 3. Ключи и секреты

Всё — в `deploy/.env.production` на сервере (шаблон — `deploy/production.env.example`; файл не коммитится, права `600`).

```bash
cd ~/hms
cp deploy/production.env.example deploy/.env.production && chmod 600 deploy/.env.production
openssl rand -hex 24      # → POSTGRES_PASSWORD, затем ещё раз → APP_DB_PASSWORD
openssl rand -base64 48   # → JWT_SIGNING_KEY
nano deploy/.env.production
```

| Переменная | Что это |
|---|---|
| `DOMAIN`, `LETSENCRYPT_EMAIL` | домен сайта, email для уведомлений Let's Encrypt |
| `DOMAIN_ALIASES` | дополнительные имена через пробел (обычно www. + домен) → 301 на `DOMAIN`; сертификат — на все имена |
| `POSTGRES_USER/PASSWORD` | владелец схемы (миграции, бэкап) |
| `APP_DB_USER/PASSWORD` | роль API (создаётся командой migrate) |
| `JWT_SIGNING_KEY` | ключ подписи токенов (смена завершит все сессии) |
| `SMTP_*` | почта гостиницы; `SMTP_SECURITY`: `StartTls` (587) или `SslOnConnect` (465) |
| `OWNER_*` | первый владелец (создаётся при первом migrate, пароль временный) |
| `WHATSAPP_*` | Cloud API; пусто — WhatsApp выключен, уведомления письмами |
| `BACKUP_*`, `RCLONE_REMOTE` | бэкапы (раздел 6) |

**Защита при старте.** В production API и migrate не запускаются, если остались значения разработки (ключ JWT `dev-only…`, пароли `hms_dev_only` / `owner-dev-password`) или `PublicWebUrl` не на https (`ProductionConfigurationCheck`).

### 3.1. Ключ шифрования бэкапов (age)

Бэкапы шифруются **публичным** ключом. Приватный ключ хранится **не на сервере**: тот, кто получит доступ к VPS или к хранилищу бэкапов, не сможет их прочитать.

На компьютере владельца ([age](https://github.com/FiloSottile/age) — `winget install FiloSottile.age` / `brew install age` / `apt install age`):

```bash
age-keygen -o hms-age-identity.txt
# Public key: age1...   ← эту строку — в BACKUP_AGE_RECIPIENT
```

Файл `hms-age-identity.txt` — хранить в двух местах (менеджер паролей + флешка в сейфе). **Без него бэкапы не восстановить.** Можно указать два публичных ключа через пробел (владелец + доверенное лицо).

## 4. Первый запуск

```bash
git clone <репозиторий> ~/hms && cd ~/hms
# заполнить deploy/.env.production (раздел 3)
./deploy/prod.sh check        # переменные заданы, compose-конфигурация валидна
./deploy/prod.sh release      # сборка образов, migrate, запуск; в конце — /health/ready
./deploy/prod.sh cert-init    # сертификат Let's Encrypt для DOMAIN (DNS уже указывает на сервер)
```

До `cert-init` nginx работает с временным самоподписанным сертификатом (браузер предупредит) — это нужно, чтобы nginx стартовал и ответил на проверку Let's Encrypt по HTTP.

Затем:
1. Открыть `https://DOMAIN/login`, войти владельцем (`OWNER_EMAIL` / `OWNER_PASSWORD`), сменить временный пароль.
2. Очистить `OWNER_PASSWORD` в `.env.production` (владелец уже создан).
3. Заполнить настройки гостиницы (название — «Great Hotel», адрес, телефон), номера, тарифы, сотрудников. Название сразу появится во вкладках, иконке сайта и превью ссылок.
4. Проверить письмо: регистрация гостя на сайте → письмо подтверждения доходит.
5. Сделать первый бэкап и проверку восстановления (раздел 6).

## 5. Релиз новой версии

```bash
cd ~/hms
./deploy/prod.sh backup       # бэкап перед релизом
git pull
./deploy/prod.sh release      # build --pull → migrate → up -d → проверка /health/ready
```

- Миграции применяются только командой `migrate` (в составе `release`), не при старте API.
- `migrate` каждый раз перевыдаёт права роли приложения — новые таблицы получают их сразу.
- Простой при релизе — секунды (перезапуск api/web). Пересборка образов идёт, пока работает старая версия.
- Откат: `git checkout <предыдущий тег>` + `release`. Если новая миграция несовместима со старым кодом — восстановление из бэкапа перед релизом (раздел 6.3).

Прочее: `./deploy/prod.sh status` (контейнеры и `/health/ready`), `logs [сервис]`, `down` / `up`.

## 6. Резервные копии

**Что:** БД (`pg_dump`, формат custom) + загруженные файлы (фото номеров, уборки) + манифест со сводкой (число таблиц, последняя миграция, строки ключевых таблиц) → один tar, зашифрованный age: `hms-ГГГГММДД-ЧЧММСС.tar.age` и `.sha256`.

**Когда:** ежедневно в `BACKUP_TIME` (по умолчанию 03:30, Asia/Bishkek) и вручную `./deploy/prod.sh backup`.

**Где:** `/var/backups/hms` на сервере, `BACKUP_RETENTION_DAYS` дней (14). Копия вне сервера — через rclone (раздел 6.1), `BACKUP_REMOTE_RETENTION_DAYS` дней (30).

**Контроль:** healthcheck контейнера `backup` — unhealthy, если успешного бэкапа нет 26 часов (`./deploy/prod.sh status`).

Ограничения (осознанные, P1 по security-architecture §9): точка восстановления — последний бэкап (до суток потерь); WAL-архив для восстановления на любой момент не настроен. БД и файлы копируются с разницей в секунды.

### 6.1. Копия вне сервера (rclone)

Потеря VPS не должна означать потерю данных. Хранилище выбирает заказчик (любое, что поддерживает [rclone](https://rclone.org/overview/): S3-совместимое, Backblaze B2, Google Drive, другой сервер по SFTP).

```bash
mkdir -p ~/hms/deploy/rclone
docker run --rm -it -v ~/hms/deploy/rclone:/config/rclone rclone/rclone config   # создать remote, например «offsite»
chmod 600 ~/hms/deploy/rclone/rclone.conf
# в .env.production: RCLONE_REMOTE=offsite:hms-backups
./deploy/prod.sh up && ./deploy/prod.sh backup    # в журнале: «копия вне сервера: offsite:hms-backups»
```

Ключ доступа к хранилищу лучше выдать только на запись в одну папку (без удаления — тогда `BACKUP_REMOTE_RETENTION_DAYS` настраивается правилом хранилища).

### 6.2. Проверка восстановления — раз в месяц (чек-лист)

Бэкап, который ни разу не восстанавливали, — не бэкап.

```bash
scp hms-age-identity.txt hms@сервер:~/hms-age-identity.txt   # путь — AGE_IDENTITY_HOST_FILE
./deploy/prod.sh backups                                         # выбрать свежий файл
./deploy/prod.sh restore-check hms-20261201-033000.tar.age
shred -u ~/hms-age-identity.txt                              # ключ — сразу убрать с сервера
```

`restore-check` расшифровывает архив, сверяет контрольную сумму и число файлов, восстанавливает БД во временную `hms_restore_check`, сравнивает сводку с манифестом и удаляет временную БД. Рабочие данные не затрагиваются.

- [ ] «ПРОВЕРКА ПРОЙДЕНА», сводка (брони, платежи, гости) правдоподобна
- [ ] бэкап за вчера есть и в хранилище вне сервера
- [ ] `./deploy/prod.sh status`: `backup` — healthy
- [ ] приватный ключ удалён с сервера
- [ ] дата проверки записана (журнал эксплуатации)

### 6.3. Восстановление (авария, ошибочные данные)

```bash
scp hms-age-identity.txt hms@сервер:~/hms-age-identity.txt
./deploy/prod.sh restore-apply hms-20261201-033000.tar.age     # спросит подтверждение «восстановить»
shred -u ~/hms-age-identity.txt
```

Порядок внутри: остановка api/web → **страховочный бэкап текущего состояния** → пересоздание БД из бэкапа и сверка с манифестом → замена файлов → `migrate` (права роли, новые миграции) → запуск.
Всё, что произошло после бэкапа, теряется — сверить с бумажным журналом стойки (брони, оплаты за этот период).

**Новый сервер:** раздел 2 → `git clone` → тот же `.env.production` (из надёжного места) → `./deploy/prod.sh release` → скопировать бэкап (из хранилища вне сервера) в `/var/backups/hms` → `restore-apply` → `cert-init`.

## 7. Nginx и HTTPS

Конфигурация — `deploy/nginx/` (образ собирается со своей конфигурацией; `nginx -t` выполняется при старте).

- TLS 1.2/1.3 (профиль Mozilla intermediate), HTTP/2, HSTS 1 год (без preload — решение о preload принимает владелец домена).
- Заголовки: `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, CSP `frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'`.
- **CSP без `script-src` — осознанно.** Next.js вставляет inline-скрипты; строгий CSP требует nonce на каждый запрос, а nonce несовместим со статической оболочкой страниц Cache Components / Partial Prerendering (страницы перестали бы быть статическими). XSS закрывается экранированием React и запретом пользовательского HTML (security-architecture §4). Вернуться к вопросу — при отказе от PPR или появлении поддержки в Next.js.
- Дополнительные имена (`DOMAIN_ALIASES`, www) — 301 на основной домен с сохранением пути и query (один адрес сайта: SEO, cookie сессии); блок генерирует `hms-cert` (`conf.d/hms-aliases.conf`), имена проверяются. Сертификат Let's Encrypt — один на все имена; добавили имя позже — повторить `cert-init` (`--expand`).
- Запросы с чужим `Host` (скан по IP) закрываются без ответа; TLS с чужим именем — отклоняется (`ssl_reject_handshake`).
- `X-Forwarded-For` nginx **заменяет** адресом клиента (не дописывает) — по нему лимиты входа в API, подделать нельзя.
- Лимиты по IP — только от флуда (страницы 100 запросов/с, всплеск 1000; вебхуки 10/с): персонал гостиницы выходит с одного IP, меню кабинета заранее подгружает все разделы. Точные лимиты (вход, бронирование) — в API.
- Тело запроса до 12 МБ (фото номеров через Server Actions), вебхуки — до 3 МБ.
- Стриминг Next.js (Suspense, PPR) — без буферизации (`proxy_buffering off`).
- Журнал запросов — JSON, путь без query (в query бывают токены из писем), без Referer.
- Сертификат: `cert-init` выпускает (HTTP-01, webroot), `certbot` продлевает, nginx перечитывает раз в 6 часов. OCSP stapling не включён — Let's Encrypt прекратил OCSP.

## 8. Логи и мониторинг

- Журналы всех контейнеров — `docker json-file` с ротацией (20 МБ × 5 на контейнер). Просмотр: `./deploy/prod.sh logs api`.
- API пишет структурные логи JSON (Serilog, `RenderedCompactJsonFormatter`), с `CorrelationId`; ПД в логи не пишутся.
- Внешний мониторинг доступности (рекомендуется): любой бесплатный uptime-сервис на `https://DOMAIN/` раз в 5 минут.
- Свободное место: `df -h` (бэкапы, образы). Старые образы: `docker image prune -f` после релиза.

## 9. Нагрузочный smoke

`deploy/loadtest/smoke.js` (k6): 20 одновременных посетителей ходят по главной, страницам номеров, входу + подбор номеров 40 раз в минуту (ниже лимита API 60/мин с одного IP); пороги — ошибок < 1 %, p95 страниц < 1,5 с, подбора < 2 с, статики < 300 мс.

```bash
# против сервера — с другого компьютера (не с самого VPS)
docker run --rm -i -e BASE_URL=https://DOMAIN grafana/k6:2.4.0 run - < deploy/loadtest/smoke.js
```

Результат локальной проверки (2026-11-27, prod-стек в Docker Desktop): 2681 запрос, 0 ошибок, p95 страниц 82 мс, подбора 118 мс, статики 8 мс. На VPS цифры будут выше — повторить после запуска.

## 10. Проверка production-сборки без сервера

`deploy/prod-local-check.sh` поднимает тот же `docker-compose.prod.yml` на своём компьютере: домен `localhost`, самоподписанный сертификат, Mailpit вместо SMTP (`deploy/prod-local.env`, `deploy/docker-compose.prod-local.yml` — **не для сервера**). То же делает CI (job «Production stack»).

```bash
docker compose -f deploy/docker-compose.yml stop          # dev-стек занимает порт 8025
./deploy/prod-local-check.sh up                            # https://localhost
cd frontend && E2E_BASE_URL=https://localhost E2E_IGNORE_HTTPS_ERRORS=1 \
  E2E_OWNER_INITIAL_PASSWORD=owner-prod-local-password npm run test:e2e
./deploy/prod-local-check.sh backup-check                  # бэкап → проверка восстановления
./deploy/prod-local-check.sh down                          # удалить проверочный стек и его данные
```
