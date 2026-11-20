# CLAUDE.md — Hotel Management System (HMS)

Проект: система управления гостиницей (1 отель, 10–30 номеров, Кыргызстан). Валюта KGS, часовой пояс Asia/Bishkek, интерфейс на русском (KY/EN позже). Исходное ТЗ: `docs/requirements/original-spec.md`. Проектная документация: `docs/`.

## Статус
Этап 1 (анализ и документы) завершён. Программирование **НЕ** начато. Sprint 0 (каркас, предшествует Sprint 1 из ТЗ §20) — только после слова «подтверждаю» от владельца проекта.

## Приоритет источников
ТЗ (`original-spec.md`) → решения заказчика (ниже и в `business-requirements.md`) → документы `docs/`. Если документы противоречат друг другу или ТЗ, нужно остановиться и спросить, а не выбирать молча. Принятое решение сразу заносится в соответствующий документ.

## Стек
- **Frontend:** Next.js (App Router), React, TypeScript, Tailwind CSS, shadcn/ui, Lucide, next-intl, TanStack Query, react-hook-form + zod.
- **Backend:** C#, ASP.NET Core Web API, .NET 10 LTS, EF Core (Npgsql), FluentValidation, ASP.NET Core Identity, Serilog.
- **БД:** PostgreSQL (расширение `btree_gist`).
- **Инфраструктура:** Docker, Docker Compose, Nginx, Ubuntu VPS, Git/GitHub.
- **Архитектура:** модульный монолит, Clean Architecture, REST, DI, DTO, единая обработка ошибок (ProblemDetails), структурные логи, фоновые задачи, аудит.
- **Тесты:** xUnit, FluentAssertions, Testcontainers (PostgreSQL). Инструменты для frontend утверждаются в Sprint 0.

## Правила работы (раздел 19 ТЗ — обязательны)
- Не создавать всё приложение за один раз; работать поэтапно.
- Делить проект на логические модули.
- Перед разработкой анализировать требования; сначала проектировать архитектуру.
- Не менять рабочий код без необходимости.
- Не удалять файлы без согласования.
- Не использовать вымышленные API интеграций; не использовать фиктивные платежи.
- Не выдавать незавершённую бизнес-логику за готовую.
- После каждого этапа запускать сборку и тесты; ошибки исправлять до перехода дальше.
- Объяснять важные технические решения; вести документацию; держать этот файл актуальным.
- Не раскрывать секреты и персональные данные; секреты не хранить в коде.
- Использовать Git; не выполнять разрушительные операции без разрешения.

**Отчёт после каждого этапа:** что реализовано; созданные файлы; изменённые файлы; технологии; выполненные тесты; результат сборки; оставшиеся проблемы; следующий этап.

## Ключевые решения заказчика
- Онлайн-эквайринг (MBANK, Optima) подключается обязательно, но **ПОСЛЕ** готовности веб-приложения. Сейчас — только архитектура (`IPaymentProvider`), домен платежей и ручные способы оплаты. Без имитации успешной оплаты, без выдуманных API. Онлайн-оплата включается флагом `OnlinePaymentEnabled` (по умолчанию false).
- WhatsApp Business: номер и доступы заказчик предоставит позже. Только официальный API (WhatsApp Business Platform / Cloud API). Сейчас — адаптер, шаблоны, outbox, история.
- Структуру БД можно менять по необходимости, с обоснованием в `docs/database/database-design.md`.

## Инварианты (нарушать нельзя)
- Интервал проживания `[CheckIn, CheckOut)`; пересечения запрещены exclusion constraint в БД + проверка в приложении.
- Расчёт цен и доступности — только на backend.
- Деньги — `numeric(14,2)`/`decimal`, без float; платёжные записи неизменяемы (исправления — сторно).
- Платежи и webhook идемпотентны; карточные данные не хранятся.
- Права проверяются на backend (RBAC + Permissions + проверка владения объектом).
- Все важные действия попадают в AuditLogs; ПД не пишутся в логи.
- Уборщик не видит финансы и ПД гостей; Completed не освобождает номер без настройки владельца.
- Fake-провайдер оплаты допустим только в тестовых проектах и не попадает в production-сборку.

## Структура проекта (план)
```
/                  CLAUDE.md, README.md, .gitignore
docs/              requirements, architecture, database, api, integrations, development
backend/           Hms.sln
  src/             Hms.Api, Hms.SharedKernel, Hms.Platform, Hms.Persistence (HmsDbContext + миграции),
                   Modules/{Identity,Hotel,Rooms,Pricing,Bookings,Guests,FrontDesk,
                            Housekeeping,Finance,PaymentGateways,Messaging,Reporting,Audit}
                   (каждый модуль: Domain, Application, Infrastructure, Contracts)
  tests/           Hms.UnitTests, Hms.IntegrationTests (Testcontainers), Hms.ApiTests, Hms.ArchitectureTests
frontend/          Next.js: src/{app,components,features,lib,i18n,hooks}
deploy/            docker-compose.yml, nginx/, env.example, backup/
.github/workflows/ CI: build, test, lint
```

## Команды (ПЛАН — уточнить в Sprint 0)
```bash
# backend
dotnet build backend/Hms.sln
dotnet test backend/Hms.sln                      # интеграционные тесты требуют Docker
dotnet format backend/Hms.sln --verify-no-changes
dotnet ef migrations add <Name> --project backend/src/Hms.Persistence --startup-project backend/src/Hms.Api
dotnet ef database update --project backend/src/Hms.Persistence --startup-project backend/src/Hms.Api

# frontend
cd frontend && npm ci
npm run dev | npm run build | npm run lint | npm test

# окружение (dev: db, mailpit; затем api, web)
docker compose -f deploy/docker-compose.yml up -d
```

## Соглашения
- Код и идентификаторы — на английском; UI-строки — через i18n (ru по умолчанию); комментарии и документы допустимы на русском.
- Ошибки API — ProblemDetails с кодом домена; DTO отдельно от сущностей.
- Один `HmsDbContext`, схема PostgreSQL на модуль; модули обращаются друг к другу только через `*.Contracts` (проверяют архитектурные тесты).
- Миграции в production применяются вручную при релизе.
- Каждая фича — с тестами (в т.ч. гонки, повторные webhook, IDOR, права).

## Открытые вопросы
См. `docs/development/development-roadmap.md` §6. Бизнес-правила (3–7) приняты как настраиваемые значения по умолчанию и ждут подтверждения заказчика; юридические вопросы 1–2 открыты и блокируют спринты 4, 6, 11. ER-диаграмма ждёт согласования.
