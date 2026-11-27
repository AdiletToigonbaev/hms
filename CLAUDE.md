# CLAUDE.md — Hotel Management System (HMS)

Проект: система управления гостиницей (1 отель, 10–30 номеров, Кыргызстан). Валюта KGS, часовой пояс Asia/Bishkek, интерфейс на русском (KY/EN позже). Исходное ТЗ: `docs/requirements/original-spec.md`. Проектная документация: `docs/`.

## Статус
- Этап 1 (документация), Sprint 0 (каркас) — завершены и подтверждены.
- Sprint 1 (Identity, аудит, outbox/email) — завершён, в `main`.
- Sprint 2 (настройки гостиницы, интеграции без адаптеров, номерной фонд, дизайн-основа) — завершён, в `main`.
- **Sprint 3 (тарифы, скидки, услуги, подбор номеров, расчёт цены, бронирования администратором) — завершён**, в `main`.
- Почасовые брони учтены в плане спринтов 5, 7, 8, 9, 10 (roadmap §2): календарь по часам, уборка после каждого выезда, сайт по часам, KPI по часам, напоминание за N мин.
- **Sprint 4 (гости / CRM) — завершён**, в `main`.
- **Sprint 5 (календарь по часам, перенос броней, правила отмены и неявки, фоновое истечение) — завершён**, в `main`.
- **Sprint 6 (Finance: ручные платежи, сторно, долги и штрафы, возвраты с лимитом, расходы) — завершён**, в `main`.
- **Sprint 7 (заселение, выселение, досрочный выезд, уборка, заявки о неисправностях) — завершён**, в `main`.
- **Sprint 8 (публичный сайт, бронирование гостем, кабинет гостя, SEO) — завершён**, в `main`.
- **Sprint 9 (дашборды владельца и администратора, 8 отчётов, выгрузка Excel/PDF) — завершён**, в `main`.
- **Sprint 10 (сообщения: уведомления email/WhatsApp, напоминания, история и статусы, автоответчик, вебхук, код подтверждения в WhatsApp) — завершён**, в `main` (адаптер Cloud API готов, отправка в WhatsApp — после доступов Meta, Sprint 12).
- **Sprint 11 (production: Nginx, HTTPS, бэкапы, роль БД приложения, нагрузочный smoke) — подготовлен** и проверен на https://localhost и в CI; запуск на VPS — когда будут сервер и домен (`docs/development/deployment.md`).
- Следующий: Sprint 12 (WhatsApp и онлайн-оплата — после доступов Meta и банков) — только после «подтверждаю».
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
- **Почасовые брони** — основной поток (решение 2026-11-20): брони по часам (цена часа × часы, минимум 2 ч, уборка 30 мин после брони) и по суткам; гость бронирует часы и на сайте; шаг начала — 1 час. Продление брони (часы/ночи) — если номер свободен, добавленное время по текущим ценам. Правила отмены/неявки/удержания для часов — roadmap §6.1a (неявка через 30 мин). **Штрафов по умолчанию нет** (решение 2026-11-21): все штрафы = 0, владелец включает сам; штраф 0 = отмена бесплатна в любое время. Пересечения проверяются по времени, а не по датам.
- Структуру БД можно менять по необходимости, с обоснованием в `docs/database/database-design.md`.

## Инварианты (нарушать нельзя)
- Занятость номера — по времени: `[StartsAt, BlockedUntil)` (выезд + уборка; посуточно — с часа заезда до часа выезда); пересечения запрещены exclusion constraint в БД (`tstzrange`) + проверка в приложении.
- Расчёт цен и доступности — только на backend.
- Деньги — `numeric(14,2)`/`decimal`, без float; платёжные записи неизменяемы (исправления — сторно).
- Платежи и webhook идемпотентны; карточные данные не хранятся.
- Права проверяются на backend (RBAC + Permissions + проверка владения объектом).
- Все важные действия попадают в AuditLogs; ПД не пишутся в логи.
- Паспортные данные и документы гостей не хранятся; фискальные чеки система не выдаёт (решения заказчика).
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
npm run test:e2e                                  # E2E против запущенного стека; API поднять с AUTH_RATE_LIMIT_PER_MINUTE=1000 (как в CI), иначе входы упрутся в лимит 10/мин; локально 4 воркера (больше — таймауты)

# окружение (из корня)
docker compose -f deploy/docker-compose.yml up -d --build      # db, mailpit, migrate, api, web (РАЗРАБОТКА)
docker compose -f deploy/docker-compose.yml up -d db mailpit   # только инфраструктура
node deploy/dev-data/seed.mjs                                  # ЛОКАЛЬНО: сброс тестовых данных и правдоподобный набор (неделя назад, 2 недели вперёд)

# production (на сервере; deploy/.env.production из production.env.example) — docs/development/deployment.md
./deploy/prod.sh check | release | cert-init | status | backup | backups | restore-check <файл> | restore-apply <файл>
# проверка production-сборки без сервера (https://localhost, как CI; dev-стек остановить — порт 8025)
./deploy/prod-local-check.sh up | backup-check | down
```
Проверка перед завершением этапа: backend build + format + все тесты; frontend lint + typecheck + test + build; `docker compose up`, `/health/ready` = Healthy и `npm run test:e2e`.
Dev-владелец (только локально): `owner@hms.local` / `owner-dev-password` → при первом входе смена пароля. Письма — http://localhost:8025.
E2E меняют пароль dev-владельца на `owner-new-password-1` (первый вход на чистой БД) — предупреждать пользователя, если E2E запускались на его стеке. После E2E тестовые данные можно пересоздать: `node deploy/dev-data/seed.mjs` (владелец, гостиница и настройки сохраняются; сотрудники admin@hms.local / admin-dev-password, maid@hms.local и maid2@hms.local / maid-dev-password).

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
- Правила брони из настроек — `BookingRules` (удержание, штрафы, момент неявки); штраф — `BookingPenalties` (домен). Фоновые задачи брони — `IBookingLifecycle` + `BookingLifecycleBackgroundService` (`BookingJobs:Enabled`; в интеграционных тестах выключена, тесты вызывают `RunAsync` сами). Календарь — `GET /calendar`, геометрия шкалы на frontend — `lib/bookings/timeline.ts`.
- Переплата по брони: кнопка возврата в карточке «Оплата» — выбор платежа и суммы в `lib/finance/overpayment.ts`; ссылка `/admin/payments/{id}?refund=N#refund` подставляет сумму (API всё равно проверяет лимит и остаток).
- Права: каталог и ограничения — `PermissionCatalog` (`IsAllowedFor(role, code)`, `LimitedPermissions`); права ролей настраивает владелец (`IRolePermissionsService`, `/owner/staff/roles`), сидер их не перезаписывает. Лимит — `IPermissionLimits`: личный, иначе роли.
- Финансы: модуль Finance (`IPaymentQueries` в Finance.Contracts); баланс брони — из платежей и `IBookingCharges` (Bookings.Contracts), лимит прав — `IPermissionLimits` (Identity.Contracts). Платежи не меняются и не удаляются (триггеры БД): ошибку исправляет сторно, деньги гостю — возврат. Конкурентные платежи/возвраты по одной брони/платежу — `pg_advisory_xact_lock` в транзакции.
- Номер брони «Ч-2111-07» — `BookingNumbers.Format` (домен) + счётчик за день `booking_number_counters` (атомарный upsert); уникален вместе с `NumberYear`.
- Стойка: `IFrontDeskService` (Bookings) — заселение/выселение одной транзакцией: статус номера через `IRoomStatusChanger` (Rooms.Contracts), задача уборки через `IHousekeepingTasks` (Housekeeping.Contracts), получено по брони — `IPaymentQueries.ReceivedForBookingAsync`. Переселение (`MoveAsync`) делит бронь на отрезки `booking_rooms`; текущий номер — `Booking.CurrentRoom` (не `Rooms[0]`). Уборка — модуль Housekeeping (задачи с чек-листом, шаблон, заявки); уборщик видит только свои и свободные задачи, без ПД гостя; экран уборщика — `/hk` (мобильный).
- Кабинет гостя: `/me/*` — только свои данные через `ICurrentGuest` (Guests.Contracts); чужое — 404. Брони гостя — `IGuestBookingService` (Bookings), оплаты/возвраты — `IMyFinanceService` (Finance), профиль/согласия/удаление аккаунта — `IGuestAccount` (Guests). Сайт: `components/site/*` (`SiteShell` — шапка/подвал в Suspense), параметры поиска — `lib/site/stay.ts` (`roomTypeId` — фильтр категории, `roomId` — выбранный гостем номер: гость выбирает конкретный номер, решение 2026-11-23); адрес сайта — `PUBLIC_WEB_URL` (`lib/site/url.ts`).
- Отчёты: модуль Reporting читает факты через контракты (`IBookingFacts`, `IFinanceFacts`, `IHousekeepingFacts`); формулы — чистые функции `Reporting.Domain.Kpi` (database-design §17); отчёт — единая `ReportTable` (сайт, Excel, PDF). Графики — `components/charts/charts.tsx` (без библиотек; цвета `--chart-1/2` проверены валидатором палитры), фильтр периода — `components/dashboard/period-filter.tsx`. Меню получает права пользователя (`staffNav(roles, t, permissions)`).
- Сообщения: модуль Messaging. Уведомление гостю — `IGuestNotifications.Enqueue(new GuestNotification(...))` (Messaging.Contracts) в той же единице работы, что и событие (рядом с `audit.Record`); канал выбирает Messaging (WhatsApp при согласии, подключении и одобренном шаблоне, иначе email; ошибка WhatsApp — письмо-замена). Напоминания — `IReminderScheduler` + фоновая задача (`MessagingJobs:Enabled`, в интеграционных тестах выключена). Тексты — `TemplateCatalog` (фиксированные коды, переменные `{{snake_case}}`, владелец правит в `/owner/messages`); WhatsApp — `IWhatsAppClient` (`CloudApiWhatsAppClient`, в тестах `FakeWhatsAppClient`), секреты `Integrations__WhatsApp__*`. Вебхуки — `IWebhookInbox` (Platform, `platform.webhook_events`) → outbox `webhook.{source}`. Переписка — `/admin/messages` (право `whatsapp.send`), в карточке гостя — блок «Сообщения». Обезличивание гостя стирает и переписку (`IGuestMessagesCleanup`).
- Рабочие экраны с меняющимися данными (рабочий стол, «Уборка», список броней, `/hk`) — `<AutoRefresh seconds={…} />` (router.refresh, не мешает вводу). Значки в меню — `NavItem.badge` (считает `AreaShell`). Журнал действий — `/owner/audit`, подписи действий — `lib/audit/labels.ts` (новый код аудита — добавить подпись).
- Гости: карточка в модуле Guests (`IGuestDirectory`, `IGuestRegistration` в Guests.Contracts); бронь хранит `GuestId` + снимок контакта. ПД гостя в аудит не пишутся — только названия изменённых полей.
- UI-компоненты: `PageHeader`, `EmptyState`/`ErrorState`/`LoadingState`, `RoomStatusBadge`, `SelectField`, `BookingStatusBadge`, `PriceBreakdown`; меню персонала — `lib/nav.ts` (по роли).
- Операции, которые нельзя выполнить дважды (бронь, позже платежи, заселение), — `.WithIdempotency("scope")` (Hms.Platform.Http): обязательный `Idempotency-Key`; frontend передаёт ключ через `api(..., { headers })` и обновляет его после отказа API.
- Модули читают номерной фонд через `IRoomDirectory` (Rooms.Contracts), цену — через `IPriceCalculator` (Pricing.Contracts); Rooms узнаёт о бронях через `IRoomBookingQueries` (Bookings.Contracts).
- Production: API работает под ролью БД без DDL и без изменения аудита (`DatabaseAppRole`, создаёт `migrate` по `Database:AppRole:*`); интеграционные тесты — под такой же ролью (`PostgresFixture.AppConnectionString`; служебный SQL в тестах — `ConnectionString` владельца, релиз в тесте — `db.MigrateAsync`). Новые требования к правам БД (новая схема — автоматически) проверять тестами под этой ролью. В production не допускаются значения разработки (`ProductionConfigurationCheck`). Nginx — `deploy/nginx/` (`add_header` только на уровне server; лимиты — от флуда, точные — в API).
- Бренд: название гостиницы — только из настроек. Монограмма — `Brand`/`brandLetter`; иконки и картинка для ссылок — `app/icon.tsx`, `app/apple-icon.tsx`, `app/og-image` (`lib/brand-image.tsx`, шрифт Lora woff в `assets/fonts`; у каждого набора символов своё имя шрифта). Не класть `opengraph-image` файлом в корень — перекроет фото номеров. Название в метаданных layout — `getCachedHotelName` (`use cache`, тег `hotel-name`, сброс `updateTag` при сохранении сведений о гостинице); `getHotelInfo` в layout-метаданных ломает сборку (Cache Components).
- Письма: модули передают только содержимое (фрагмент HTML + текст, кнопка — `EmailLayout.Button`); шапку с названием, подвал с контактами и имя отправителя добавляет `IEmailComposer` (Platform) при отправке — в outbox-обработчике писем и в Messaging. Сведения о гостинице — `IEmailBrandProvider` (реализация в Hotel). Не собирать `<html>` письма в модуле.
- Текущее время в серверных компонентах — только `await requestNow()` (`lib/time/request-now.ts`, внутри `connection()`), результат передавать в `hotelToday/hotelHour/hotelNowLocal/windowLeft` явно: `new Date()` при пререндере (в т.ч. предзагрузка, partialPrefetching) — ошибка Cache Components.
- Сбой страницы — `app/error.tsx` и `app/global-error.tsx` (`CrashScreen`). Цвет панели браузера — `viewport` из `lib/theme-color.ts` (сайт — `siteViewport`, кабинеты — `areaViewport`). Ярлык «На экран Домой» — кабинеты персонала `/owner`, `/admin`, `/hk` (`staffAppManifest`/`staffAppMetadata` в `lib/app-manifest.ts`: свой id и стартовая страница, scope — весь сайт; значки `/app-icon/{192|512}`); гостям манифест не отдаётся.
- Сырой SQL в миграциях (exclusion constraint, generated columns) — с комментарием и симметричным `Down`. Дочерние сущности с Id из домена — `ValueGeneratedNever()`, иначе EF сделает UPDATE вместо INSERT.

## Открытые вопросы
См. `docs/development/development-roadmap.md` §6. Бизнес-правила (3–7) приняты как настраиваемые значения по умолчанию и ждут подтверждения заказчика; юридические вопросы 1–2 решены (2026-11-20; срок хранения данных гостя — по умолчанию, ждёт подтверждения). ER-диаграмма ждёт согласования.
