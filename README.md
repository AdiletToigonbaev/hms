# HMS — система управления гостиницей

Веб-приложение для гостиницы на 10–30 номеров (Кыргызстан, KGS, Asia/Bishkek): бронирование, заселение/выселение, уборка, финансы, аналитика, публичный портал, WhatsApp.

- Исходное ТЗ: [`docs/requirements/original-spec.md`](docs/requirements/original-spec.md)
- Документация проекта: [`docs/`](docs/)
- Правила разработки: [`CLAUDE.md`](CLAUDE.md)

**Статус:** Sprint 0 — каркас приложения. Бизнес-функций пока нет.

## Стек
| Часть | Технологии |
|---|---|
| Backend | .NET 10 LTS, ASP.NET Core, EF Core + Npgsql, Serilog, модульный монолит |
| Frontend | Next.js 16 (App Router), React 19, TypeScript, Tailwind CSS 4, shadcn/ui, Lucide, next-intl, TanStack Query |
| БД | PostgreSQL 18 |
| Инфраструктура | Docker, Docker Compose, Nginx (Sprint 11), GitHub Actions |

## Требования для разработки
- .NET SDK 10.0.404+ (версия зафиксирована в `backend/global.json`)
- Node.js 24 LTS+ и npm 11+
- Docker Desktop (для БД, интеграционных тестов и запуска стека)

## Быстрый старт: весь стек в Docker
```bash
docker compose -f deploy/docker-compose.yml up -d --build
```
| Сервис | Адрес |
|---|---|
| Web (Next.js) | http://localhost:3000 |
| API | http://localhost:5080 (`/health/live`, `/health/ready`, OpenAPI: `/openapi/v1.json`) |
| Mailpit (письма) | http://localhost:8025 |
| PostgreSQL | localhost:5432, БД/пользователь `hms` |

Миграции применяются одноразовым сервисом `migrate` перед стартом API. Порты открыты только на `127.0.0.1`.
Параметры БД можно переопределить в `deploy/.env` (образец — `deploy/env.example`). Пароль по умолчанию `hms_dev_only` предназначен **только для локальной разработки**.

Остановка: `docker compose -f deploy/docker-compose.yml down` (с удалением данных: `down -v`).

## Разработка без Docker для приложений
```bash
# 1. Инфраструктура
docker compose -f deploy/docker-compose.yml up -d db mailpit

# 2. Backend (http://localhost:5080)
cd backend
dotnet tool restore
dotnet ef database update --project src/Hms.Persistence --startup-project src/Hms.Api
dotnet run --project src/Hms.Api

# 3. Frontend (http://localhost:3000), в другом терминале
cd frontend
npm ci
npm run dev
```

## Проверки (то же, что в CI)
```bash
# Backend
cd backend
dotnet build Hms.sln
dotnet format Hms.sln --verify-no-changes
dotnet test --solution Hms.sln                       # все тесты; интеграционные требуют Docker
dotnet test --project tests/Hms.UnitTests            # отдельный проект

# Frontend
cd frontend
npm run lint && npm run typecheck && npm test && npm run build
```

## Миграции БД
```bash
cd backend
dotnet ef migrations add <Name> --project src/Hms.Persistence --startup-project src/Hms.Api
dotnet ef database update      --project src/Hms.Persistence --startup-project src/Hms.Api
```
В production миграции применяются отдельной командой при релизе: `dotnet Hms.Api.dll migrate` (в Docker — сервис `migrate`).

## Структура
```
backend/   .NET solution: src/ (Api, SharedKernel, Platform, Persistence, Modules/ — с Sprint 1), tests/
frontend/  Next.js: src/app, components, lib, i18n; messages/ — тексты интерфейса
deploy/    docker-compose.yml (разработка), env.example; production-конфигурация — Sprint 11
docs/      требования, архитектура, БД, API, интеграции, план
```

## Развёртывание на сервере
Будет описано в Sprint 11 (Nginx, HTTPS, домен, бэкапы, production-переменные окружения).
