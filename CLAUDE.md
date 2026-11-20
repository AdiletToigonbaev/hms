# CLAUDE.md — HMS (Hotel Management System)

Система управления гостиницей на 10–30 номеров (Кыргызстан): бронирование, заселение/выселение, уборка, финансы, аналитика, публичный портал, WhatsApp.
Текущий этап: **документация утверждается (этап 1)**. Код ещё не написан. Sprint 0 начинается только после явного подтверждения заказчика («подтверждаю»).

## Источники истины
| Документ | Назначение |
|---|---|
| `docs/requirements/original-spec.md` | Исходное ТЗ — главный источник. **Сейчас файл пустой — TODO: добавить текст ТЗ** |
| `docs/requirements/business-requirements.md` | Цели, параметры, решения заказчика |
| `docs/requirements/functional-requirements.md` | Требования по модулям с приоритетами P0/P1/P2 |
| `docs/architecture/system-architecture.md` | Модули, слои, ключевые решения |
| `docs/architecture/security-architecture.md` | Аутентификация, матрица прав, ПД |
| `docs/database/database-design.md`, `er-diagram.md` | Схема БД, ограничения, формулы KPI |
| `docs/api/api-specification.md` | REST API v1 |
| `docs/integrations/*.md` | Эквайринг MBANK/Optima, WhatsApp |
| `docs/development/development-roadmap.md` | Спринты, зависимости, открытые вопросы |

Если ТЗ и документы расходятся, приоритет у ТЗ. Если расходятся документы между собой, надо остановиться и спросить заказчика, а не выбирать молча. Любое изменённое решение сразу вносится в соответствующий документ.

## Стек
- **Backend:** .NET 10, ASP.NET Core, модульный монолит; EF Core + Npgsql; ASP.NET Core Identity; JWT access (15 мин) + refresh-токен в httpOnly cookie с ротацией; FluentValidation; Serilog (JSON, correlation id); ProblemDetails (RFC 9457); OpenAPI.
- **БД:** PostgreSQL (расширение `btree_gist`), по схеме на модуль.
- **Frontend:** Next.js (App Router, TypeScript), SSR для публичных страниц; TanStack Query, react-hook-form + zod, next-intl (ru по умолчанию; ky, en позже), shadcn/ui; типы API генерируются из OpenAPI; BFF-маршруты для cookie.
- **Тесты:** xUnit, FluentAssertions, Testcontainers (PostgreSQL); frontend: TODO (Vitest + Playwright — предложение, утвердить в Sprint 0).
- **Экспорт:** ClosedXML (xlsx), QuestPDF (pdf).
- **Инфраструктура:** Docker Compose (nginx, web, api, db, backup), Let's Encrypt, один VPS Ubuntu; CI на GitHub Actions.

## Структура репозитория (план)
```
/
├─ CLAUDE.md, README.md
├─ docs/                       документация (этап 1)
├─ backend/
│  ├─ Hms.sln
│  ├─ src/
│  │  ├─ Hms.Api/              host, composition root, middleware, auth, OpenAPI
│  │  ├─ Hms.SharedKernel/     Result, Money, DateRange, базовые классы, события
│  │  ├─ Hms.Platform/         идемпотентность, jobs, storage, аудит-перехватчик
│  │  └─ Modules/<Module>/{Domain,Application,Infrastructure,Contracts}
│  │       Identity, Hotel, Rooms, Pricing, Bookings, Guests, FrontDesk,
│  │       Housekeeping, Finance, PaymentGateways, Messaging, Reporting, Audit
│  └─ tests/ Hms.UnitTests, Hms.IntegrationTests (Testcontainers), Hms.ApiTests
├─ frontend/src/{app,components,features,lib,i18n,hooks}
│     app: (public)/ (guest)/ (staff)/admin (owner)/owner (housekeeper)/hk
├─ deploy/ {docker-compose.yml, nginx/, env.example, backup/}
└─ .github/workflows/          CI: build, test, lint
```

## Команды (план, уточняются в Sprint 0)
```bash
# Окружение для разработки
docker compose -f deploy/docker-compose.yml up -d        # весь стек
docker compose -f deploy/docker-compose.yml up -d db     # только PostgreSQL

# Backend
dotnet restore backend/Hms.sln
dotnet build backend/Hms.sln -c Release
dotnet test backend/Hms.sln                               # unit + integration (нужен Docker)
dotnet test backend/tests/Hms.UnitTests                   # только unit
dotnet format backend/Hms.sln --verify-no-changes         # линт/формат
dotnet run --project backend/src/Hms.Api

# Миграции EF Core (модель контекстов уточняется, см. вопросы)
dotnet ef migrations add <Name> -p backend/src/Modules/<Module>/Infrastructure -s backend/src/Hms.Api
dotnet ef database update -s backend/src/Hms.Api          # в production только отдельной командой при релизе

# Frontend (менеджер пакетов утвердить в Sprint 0; ниже npm)
cd frontend && npm ci
npm run dev | npm run build | npm run lint | npm run typecheck | npm test
npm run gen:api                                           # типы из OpenAPI
```

## Правила разработки
### Из раздела 19 ТЗ
> **TODO:** текст ТЗ отсутствует (`original-spec.md` пустой). После добавления ТЗ перенести сюда правила §19 дословно или близко к тексту, включая формат отчёта по спринту.

### Процесс
- Работа вертикальными срезами по спринтам из `development-roadmap.md`. Следующий спринт начинается только после подтверждения заказчика.
- Фича без тестов не считается готовой. В конце спринта должны проходить сборка, тесты и миграции; документация и этот файл обновлены; сдан отчёт по формату §19 ТЗ.
- Сначала схема и согласование (ER-диаграмму утверждает заказчик), потом код.
- Не выдумывать то, чего нет в официальной документации: API банков, параметры WhatsApp Cloud API, юридические нормы. Неизвестное фиксируется как открытый вопрос.

### Домен и данные
- Цена и доступность считаются только на backend. Цена брони фиксируется снимком (`PriceSnapshot`).
- Защита от двойного бронирования: `EXCLUDE USING gist (RoomId WITH =, StayRange WITH &&) WHERE (IsBlocking)` + транзакция; при нарушении — 409.
- Проживание задаётся интервалом `[CheckIn, CheckOut)`, тип `date` в зоне Asia/Bishkek. События хранятся в `timestamptz` (UTC). Текущее время берётся только через `IHotelClock`.
- Деньги: `numeric(14,2)` и тип `Money`, валюта KGS; `double`/`float` для денег запрещены. Суммы всегда > 0, направление задаёт тип операции.
- Платежи, транзакции, история и аудит не изменяются и не удаляются; исправления только сторно/корректировкой. Для остальных сущностей soft delete или архив.
- `HotelId` во всех бизнес-таблицах + глобальный query filter.
- Модули обращаются друг к другу только через `*.Contracts` и доменные события, без доступа к чужим таблицам.
- Конкурентность: `RowVersion` (xmin) на Bookings, Payments, Rooms. `Idempotency-Key` обязателен на POST для броней, платежей, check-in/out.
- Внешние отправки (WhatsApp) и доменные события идут через outbox в той же транзакции.

### Безопасность
- Права проверяются на backend через permission-policies и объектные проверки (IDOR). Скрытие в UI защитой не считается.
- Уборщик не видит финансы и ПД гостей.
- Данные банковских карт система не принимает, не хранит и не логирует. ПД в логах маскируются; номера документов шифруются на уровне приложения.
- Секреты хранятся только в переменных окружения или Docker secrets; `.env` не коммитится.

### Интеграции
- **Заглушки-имитаторы успешной оплаты запрещены (требование ТЗ).** Fake-провайдер допустим только в тестовых проектах и не попадает в production-сборку.
- Бронь становится `Confirmed` по онлайн-оплате только после проверенного webhook или проверенного запроса статуса у банка, а не по редиректу браузера.
- WhatsApp подключается только через официальный WhatsApp Business Platform / Cloud API. Сообщения отправляются только при наличии согласия; вне 24-часового окна — только утверждёнными шаблонами.
- Онлайн-оплата включается флагом `OnlinePaymentEnabled` (по умолчанию false).

### Код и UI
- Все строки UI берутся из i18n-ресурсов, хардкод текста не допускается.
- Ошибки возвращаются как `application/problem+json` с доменным кодом (`booking.room_unavailable` и т.п.).
- Интерфейс адаптивный; интерфейс уборщика делается mobile-first.
