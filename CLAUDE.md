# CLAUDE.md — Hotel Management System (HMS)

Проект: система управления гостиницей (1 отель, 10–30 номеров, Кыргызстан). Валюта KGS, часовой пояс Asia/Bishkek, интерфейс на русском (KY/EN позже). Исходное ТЗ: `docs/requirements/original-spec.md`. Проектная документация: `docs/`.

## Статус
- Этап 1 (документация), Sprint 0 (каркас) — завершены и подтверждены.
- Sprint 1 (Identity, аудит, outbox/email) — завершён, в `main`.
- Sprint 2 (настройки гостиницы, интеграции без адаптеров, номерной фонд, дизайн-основа) — завершён, в `main`.
- **Sprint 3 (тарифы, скидки, услуги, подбор номеров, расчёт цены, бронирования администратором) — завершён** в ветке `sprint/3-bookings`, ожидает подтверждения заказчика.
- Следующий: Sprint 4 (гости / CRM) — только после подтверждения заказчика; юридические вопросы 1–2 блокируют его.
- Журнал спринтов и отклонения от плана: `docs/development/development-roadmap.md` §8.

## Приоритет источников
ТЗ (`original-spec.md`) → решения заказчика (ниже и в `business-requirements.md`) → документы `docs/`. Если документы противоречат друг другу или ТЗ, нужно остановиться и спросить, а не выбирать молча. Принятое решение сразу заносится в соответствующий документ.

## Стек
- **Frontend:** Next.js 16 (App Router, Cache Components), React 19, TypeScript, Tailwind CSS 4, shadcn/ui (Base UI), Lucide, next-intl, TanStack Query, react-hook-form + zod. **Next.js 16 отличается от прежних версий — перед изменениями сверяться с `frontend/node_modules/next/dist/docs/` (см. `frontend/AGENTS.md`).**
- **Backend:** C#, ASP.NET Core Web API, .NET 10 LTS (SDK закреплён в `backend/global.json`), EF Core + Npgsql (snake_case-имена в БД), FluentValidation, ASP.NET Core Identity, Serilog. Версии пакетов — централизованно в `backend/Directory.Packages.props`; для пакетов Microsoft — только ветка 10.x.
- **БД:** PostgreSQL 18 (расширение `btree_gist`).
- **Инфраструктура:** Docker, Docker Compose, Nginx, Ubuntu VPS, Git/GitHub, GitHub Actions.
- **Архитектура:** модульный монолит, Clean Architecture, REST, DI, DTO, единая обработка ошибок (ProblemDetails), структурные логи, фоновые задачи, аудит.
- **Тесты:** backend — xUnit v3 на Microsoft.Testing.Platform, AwesomeAssertions (форк FluentAssertions под Apache 2.0: FluentAssertions 8 платный для коммерческих проектов), Testcontainers, NetArchTest. Frontend — Vitest + Testing Library.

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
frontend/          Next.js: src/{app,components,features,lib,i18n,hooks}; messages/ — тексты UI
deploy/            docker-compose.yml, nginx/, env.example, backup/
.github/workflows/ CI: build, test, lint
```

## Команды (проверены в Sprint 0)
```bash
# backend (из каталога backend/)
dotnet build Hms.sln                              # предупреждения = ошибки
dotnet format Hms.sln --verify-no-changes
dotnet test --solution Hms.sln                    # все тесты; интеграционные требуют запущенный Docker
dotnet test --project tests/Hms.UnitTests         # один проект (синтаксис Microsoft.Testing.Platform)
dotnet tool restore                               # локальный dotnet-ef 10.x
dotnet ef migrations add <Name> --project src/Hms.Persistence --startup-project src/Hms.Api
dotnet ef database update --project src/Hms.Persistence --startup-project src/Hms.Api
dotnet run --project src/Hms.Api -- migrate       # миграции + сидеры (роли, права, первый владелец) локально
dotnet run --project src/Hms.Api                  # http://localhost:5080
dotnet Hms.Api.dll migrate                        # то же при релизе (в Docker — сервис migrate)

# frontend (из каталога frontend/)
npm ci
npm run dev | npm run lint | npm run typecheck | npm test | npm run build
npx playwright install chromium                   # один раз
npm run test:e2e                                  # E2E против запущенного стека (docker compose up)

# окружение (из корня)
docker compose -f deploy/docker-compose.yml up -d --build      # db, mailpit, migrate, api, web
docker compose -f deploy/docker-compose.yml up -d db mailpit   # только инфраструктура
```
Проверка перед завершением этапа: backend build + format + все тесты; frontend lint + typecheck + test + build; `docker compose up`, `/health/ready` = Healthy и `npm run test:e2e`.
Dev-владелец (только локально): `owner@hms.local` / `owner-dev-password` → при первом входе смена пароля. Письма — http://localhost:8025.
E2E меняют пароль dev-владельца на `owner-new-password-1` (первый вход на чистой БД) — предупреждать пользователя, если E2E запускались на его стеке.

## Дизайн
Тёплый «гостиничный» стиль — `docs/design/design-direction.md` (палитра, шрифты Lora + Manrope, статусные цвета). Название гостиницы не зашивать в интерфейс — брать из настроек отеля; логотип — один компонент `Brand`.

## Соглашения
- Код и идентификаторы — на английском; UI-строки — через i18n (ru по умолчанию); комментарии и документы допустимы на русском.
- Ошибки API — ProblemDetails с кодом домена; DTO отдельно от сущностей.
- Один `HmsDbContext`, схема PostgreSQL на модуль; модули обращаются друг к другу только через `*.Contracts` (проверяют архитектурные тесты).
- Миграции в production применяются вручную при релизе.
- Каждая фича — с тестами (в т.ч. гонки, повторные webhook, IDOR, права).
- Модульные сборки именуются `Hms.Modules.{Module}.{Domain|Application|Infrastructure|Contracts}` — по этому имени их находят архитектурные тесты.
- Модуль вносит свои сущности в `HmsDbContext` через реализацию `IModelConfigurator` (своя схема PostgreSQL).
- Доменные ошибки — `Error`/`Result` из `Hms.SharedKernel`; в HTTP — через `ToProblem()`/`ToHttpResult()` (`Hms.Api/Infrastructure`).
- Текущее время — только `IHotelClock` (UTC для событий, `Today` — дата отеля). Деньги — `Money`, проживание — `DateRange`.
- Frontend: деньги и даты — только через `src/lib/format.ts`; браузер ходит в API только через Next.js.
- npm 11 блокирует install-скрипты пакетов; разрешение/запрет — осознанно, в `package.json` → `allowScripts`.
- Backend-эндпоинты: группа модуля в `Map{Module}Endpoints`, права — `.RequirePermission(Permissions.X)` (коды в `Hms.Modules.Identity.Contracts`, описания и права ролей — `PermissionCatalog`), тело запроса — `.WithValidation<T>()` (FluentValidation, сообщения на русском).
- Аудит значимых действий — `IAuditTrail.Record(...)` в той же единице работы, что и изменение; письма и внешние отправки — только через outbox (`IEmailOutbox`, `IOutbox`).
- Frontend: изменения — только Server Actions (CSRF-защита Next.js); каждое действие само полагается на проверку прав в API. Чтение — серверные компоненты через `lib/api/server.ts`. Сессия — httpOnly-cookie (`lib/auth/cookies.ts`), обновление токенов — `src/proxy.ts`. Чтение cookies/`searchParams` — только внутри `<Suspense>` (Cache Components).
- В matcher `proxy.ts` нельзя писать `\\.` — экранирование теряется при сборке (используйте `[.]`; есть регрессионный тест).
- E2E: Next.js 16 держит предыдущие страницы в DOM скрытыми (`<Activity>`) — локаторы по точному имени (`exact: true`) и по ролям.
- Гостиница одна: `HotelId` и настройки — только через `IHotelContext` (Hotel.Contracts); имена пользователей для показа — `IUserDirectory` (Identity.Contracts).
- Чтение несколькими ролями — `.RequireAnyPermission(...)`; изменение — `.RequirePermission(...)`. Конкурентные правки — `version` (xmin) в запросе, конфликт → `409 concurrency.conflict`.
- Файлы: загрузка только через `IFileService` (проверка сигнатуры, лимит); браузер получает их через маршрут Next.js `/media/{id}`, а не напрямую из API.
- UI-компоненты: `PageHeader`, `EmptyState`/`ErrorState`/`LoadingState`, `RoomStatusBadge`, `SelectField`; меню персонала — `lib/nav.ts` (по роли)., `BookingStatusBadge`, `PriceBreakdown`.
- Операции, которые нельзя выполнить дважды (бронь, позже платежи, заселение), — `.WithIdempotency("scope")` (Hms.Platform.Http): обязательный `Idempotency-Key`; frontend передаёт ключ через `api(..., { headers })` и обновляет его после отказа API.
- Модули читают номерной фонд через `IRoomDirectory` (Rooms.Contracts), цену — через `IPriceCalculator` (Pricing.Contracts); Rooms узнаёт о бронях через `IRoomBookingQueries` (Bookings.Contracts).
- Сырой SQL в миграциях (exclusion constraint, generated columns) — с комментарием и симметричным `Down`. Дочерние сущности с Id из домена — `ValueGeneratedNever()`, иначе EF сделает UPDATE вместо INSERT.

## Открытые вопросы
См. `docs/development/development-roadmap.md` §6. Бизнес-правила (3–7) приняты как настраиваемые значения по умолчанию и ждут подтверждения заказчика; юридические вопросы 1–2 открыты и блокируют спринты 4, 6, 11. ER-диаграмма ждёт согласования.
