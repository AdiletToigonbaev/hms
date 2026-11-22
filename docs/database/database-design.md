# Проектирование базы данных (PostgreSQL)

**Именование:** в документах таблицы и поля названы в PascalCase (логические имена, совпадают с C#-сущностями). Физические имена в PostgreSQL — snake_case (`booking_rooms.stay_range`), их формирует EFCore.NamingConventions; сырой SQL пишется в snake_case.

Все бизнес-таблицы содержат `Id (uuid)`, `HotelId`, `CreatedAt`, `UpdatedAt`; где нужно — `CreatedBy`, `RowVersion` (xmin), `IsDeleted` (soft delete). Деньги `numeric(14,2)`, валюта `KGS`. Диаграмма: `er-diagram.md`.

## 1. Изменения относительно списка из ТЗ (с обоснованием)
| Решение | Причина |
|---|---|
| Добавлены `RolePermissions`, `UserPermissions` | права администратора настраиваются владельцем |
| Добавлена `RefreshTokens` | ротация и отзыв токенов |
| Добавлены `Amenities`, `RoomTypeAmenities`, `RoomPhotos` вместо одной `RoomAmenities` | справочник удобств, связь M:N, фото |
| Добавлены `RoomRates`, `Discounts`, `Services`, `BookingServices` | тарифы, скидки и доп. услуги по ТЗ |
| Добавлена `GuestConsents` | согласия на ПД и WhatsApp |
| Добавлены `ChecklistTemplates`, `ChecklistTemplateItems` | `HousekeepingChecklist` ТЗ = экземпляр; шаблон нужен отдельно |
| `HousekeepingChecklist` переименована в `HousekeepingChecklistItems` | элементы по задаче |
| Добавлена `WebhookEvents` | сырое хранение и идемпотентность вебхуков банков и WhatsApp |
| Добавлена `IdempotencyKeys` | идемпотентность операций API |
| Служебные таблицы ASP.NET Identity (`user_claims`, `user_logins`, `user_tokens`, `role_claims`) | обязательны для хранилища Identity; `user_tokens` — для 2FA (P1) |
| Добавлена `data_protection_keys` | ключи подписи одноразовых ссылок из писем должны переживать перезапуск и быть общими для экземпляров API |
| Добавлена `OutboxMessages` | надёжная доставка WhatsApp/email и интеграционных событий в одной транзакции с бизнес-изменением (см. архитектуру §6) |
| Добавлена `IntegrationSettings` | ТЗ §3.1 «настройка платёжных интеграций и WhatsApp»: включение, режим Sandbox/Production, несекретные параметры, статус проверки. Секреты в БД не хранятся |
| Добавлены `ExpenseCategories`, `StoredFiles` | расходы по категориям; единое хранение вложений/фото |
| `BookingRooms` содержит `StayRange` | exclusion constraint против пересечений |
| `PaymentTransactions` отдельно от `Payments` | `Payment` — бизнес-намерение, `Transaction` — каждая попытка/событие у провайдера |
| `Users` = таблица Identity (`AspNetUsers`, переименована в `identity.Users`); профиль гостя — `Guests` (связь `Guests.UserId`). Отдельного профиля персонала нет | не дублировать Identity; персоналу достаточно полей Users (ФИО, телефон, тип) |
| `Payments.Channel` + тип `Reversal` | различать ручную регистрацию и эквайринг для одних и тех же MBank/Optima; сторно вместо изменения записей (ТЗ: история финансовых операций) |

## 2. Таблицы по модулям

### Identity
- **users** (таблица ASP.NET Identity в схеме `identity`): Id, UserName (= Id, служебное), Email + NormalizedEmail (уникальный, если задан), PhoneNumber E.164 (уникальный, если задан), PasswordHash, SecurityStamp, ConcurrencyStamp, EmailConfirmed, Lockout*, AccessFailedCount, FullName, UserType (Staff|Guest), IsActive, MustChangePassword, CreatedAt, LastLoginAt, DeactivatedAt.
- **roles** (Owner, Administrator, Housekeeper, Guest), **user_roles**.
- **user_claims, user_logins, user_tokens, role_claims** — служебные таблицы хранилища ASP.NET Identity (обязательны для его UserStore/RoleStore; `user_tokens` понадобится для 2FA). Бизнес-данных не содержат.
- **permissions** (Code PK, Group, Description, OwnerOnly) — синхронизируется с каталогом в коде при каждом `migrate`. **role_permissions** (RoleId, PermissionCode) — права роли по умолчанию, тоже синхронизируются. **user_permissions** (UserId, PermissionCode, IsGranted, LimitAmount numeric(14,2) null) — индивидуальные настройки владельца, сидер их не трогает.
- **refresh_tokens** (Id, UserId, FamilyId — сессия, TokenHash SHA-256 unique, CreatedAt, ExpiresAt, RevokedAt, RevokeReason Rotated|Logout|ReuseDetected|PasswordChanged|UserDeactivated|AdminReset, ReplacedByTokenId, CreatedByIp, UserAgent).
- Справочник `Hotels` и `HotelId` в таблицах Identity появятся в Sprint 2 вместе с модулем Hotel.

### Hotel
- **Hotels**(Name, Address, Phone, TimeZone, Currency).
- **HotelSettings**(HotelId PK, CheckInTime, CheckOutTime, OnlinePaymentEnabled, PrepaymentPercent, PaymentHoldMinutes, PendingHoldHours, AutoConfirmWebBookings, AutoReleaseRoomAfterCleaning, RequireEmailConfirmation, NoShowCutoffTime, CancellationPolicy jsonb {FreeCancelHoursBeforeCheckIn, LateCancelPenaltyNights, NoShowPenaltyNights, EarlyDeparturePenaltyNights}, Rules text, Locale…). Значения по умолчанию — `development/development-roadmap.md` §6.
- **IntegrationSettings**(HotelId, Provider MBank|Optima|WhatsApp|Email, IsEnabled, Mode Sandbox|Production, PublicConfig jsonb (merchant id, phone number id и т. п. — без секретов), LastCheckAt, LastCheckStatus). Unique (HotelId, Provider).

### Rooms
- **RoomTypes**(Name, Description, Capacity, BedsCount, BedType, BasePrice, IsActive).
- **Rooms**(RoomTypeId, Number, Floor, Capacity null, BedsCount null, BedType null, PriceOverride null, Description, Status Available|Occupied|Cleaning|Maintenance|OutOfService, IsArchived). Unique (HotelId, Number). Поля вместимости/кроватей null = берутся из категории.
- **Amenities**(Code, Name), **RoomTypeAmenities**, **RoomPhotos**(RoomTypeId null, RoomId null, FileId, SortOrder). Check: заполнено ровно одно из RoomTypeId/RoomId.
- **RoomStatusHistory**(RoomId, FromStatus, ToStatus, Reason, ChangedBy, ChangedAt, BookingId null).

### Pricing
- **RoomRates**(RoomTypeId, DateFrom, DateTo, Price, DaysOfWeek mask, Priority).
- **Discounts**(Code null, Type %|amount, Value, ValidFrom/To, MinNights, IsActive).
- **Services**(Name, Price, IsPerNight, IsActive).

### Guests
- **Guests**(UserId null, FullName, Phone, WhatsAppPhone, Email, BirthDate null, RegisteredAt, AdminNotes, VisitsCount, IsDeleted).
- ~~GuestDocuments~~ — **не создаётся** (решение заказчика 2026-11-20: паспортные данные не храним; ТЗ требует их только «при наличии законного основания»). При появлении основания — отдельная миграция с шифрованием номера и ограниченным доступом.
- **GuestConsents**(GuestId, Type: PersonalData|WhatsApp, GrantedAt, RevokedAt, Source).

### Bookings
- **Bookings**(Number unique human-readable, GuestId, Status, Source Web|Admin|WhatsApp, CheckIn date, CheckOut date, AdultsCount, ChildrenCount, DiscountId null, TotalAmount, DiscountAmount, ServicesAmount, PenaltyAmount, PaidAmount (денорм., пересчёт из платежей), PriceSnapshot jsonb, ConfirmDueAt null, PaymentDueAt null, CancelledAt, CancelReason, ActualCheckInAt, ActualCheckOutAt, Comment, RowVersion).
- **BookingRooms**(BookingId, RoomId, RoomTypeId, PricePerNight, StayRange daterange, IsBlocking bool). **EXCLUDE USING gist (RoomId WITH =, StayRange WITH &&) WHERE (IsBlocking)**. IsBlocking = статус брони ∈ {Pending, AwaitingPayment, Confirmed, CheckedIn}.
- **BookingGuests**(BookingId, GuestId, IsPrimary).
- **BookingServices**(BookingId, ServiceId, Qty, UnitPrice, AddedAt, AddedBy).
- **BookingHistory**(BookingId, EventType, OldValues jsonb, NewValues jsonb, ChangedBy, ChangedAt, Comment).
- Check: `CheckOut > CheckIn`, `Adults ≥ 1`.

### Finance
- **Payments**(BookingId null, GuestId, Type Prepayment|Stay|Service|Other|Reversal, Method Cash|BankTransfer|CardPos|MBank|Optima, Channel Manual|Online, Amount, Status, Provider null, ExternalId null, ReversalOfPaymentId null, RefundedAmount (денорм. из Refunds), PaidAt, RegisteredBy null (ручной), Comment, RowVersion). Unique (Provider, ExternalId) where not null. Неизменяемы все поля, кроме Status/RefundedAmount/RowVersion; каждое изменение статуса пишет строку в PaymentTransactions. Ручной платёж создаётся сразу в `Paid`. Unique (ReversalOfPaymentId) where not null — сторно не более одного.
- **PaymentTransactions**(PaymentId, Provider null, Kind Charge|Refund|Reversal|StatusChange, Amount, Status, ProviderTxnId null, RawRequest/Response jsonb без карточных данных, OccurredAt). Append-only. Unique (Provider, ProviderTxnId, Kind) where ProviderTxnId not null.
- **Refunds**(PaymentId, BookingId, Amount, Reason, Status Requested|Approved|Rejected|Paid, Method, RequestedBy, RequestedByGuest bool, ApprovedBy, DecidedAt, PaidAt). **Источник истины по возвратам**; статус `Payments` (RefundPending/Refunded/PartiallyRefunded) выводится из Refunds в той же транзакции. Check: Σ не отклонённых возвратов ≤ Payment.Amount (проверка в приложении под `FOR UPDATE`).
- **Expenses**(CategoryId, Amount, Date, Supplier, Description, FileId null, CreatedBy, IsVoided).
- **ExpenseCategories**.
- Check: `Amount > 0`; отрицательные суммы не используются, направление задаёт тип.

### Housekeeping
- **ChecklistTemplates**(RoomTypeId null, Name) / **ChecklistTemplateItems**(TemplateId, Text, SortOrder).
- **HousekeepingTasks**(RoomId, BookingId null, Status, Priority, AssignedTo null, CreatedBy|Auto, StartedAt, CompletedAt, InspectedBy, InspectedAt, RejectReason, Notes, IsDeleted).
- **HousekeepingChecklistItems**(TaskId, Text, IsDone, DoneAt).
- **MaintenanceRequests**(RoomId, ReportedBy, TaskId null, Type Defect|Damage, Description, Severity, Status, ResolvedAt, FileIds).

### Messaging
- **NotificationTemplates**(Code, Language, WaTemplateName, WaTemplateStatus, Body, Variables jsonb, IsActive).
- **WhatsAppMessages**(GuestId, Direction In|Out, WaMessageId, TemplateCode null, Body, Status Queued|Sent|Delivered|Read|Failed, Error, Timestamps, SentBy null, BookingId null).

### Platform / Audit
- **WebhookEvents**(Source MBank|Optima|WhatsApp, EventId/DedupKey, Payload jsonb, SignatureValid, ReceivedAt, ProcessedAt, Error). Unique (Source, DedupKey).
- **outbox_messages** (Type, Payload jsonb, Status Pending|Done|Failed, Attempts, CreatedAt, NextAttemptAt, ProcessedAt, LastError, DedupKey unique). Выборка — `FOR UPDATE SKIP LOCKED` (безопасно для нескольких обработчиков); пауза между попытками 10 с × 2ⁿ, не более 1 ч, не более 10 попыток; payload писем (ссылки с токенами) очищается после отправки.
- **data_protection_keys** — ключи ASP.NET Core Data Protection (подпись токенов подтверждения email и сброса пароля); в БД, чтобы ссылки из писем работали после перезапуска и на любом экземпляре API.
- **IdempotencyKeys**(Key, Scope, RequestHash, ResponseStatus, ResponseBody, ExpiresAt). Unique (Scope, Key). Отдельной колонки идемпотентности в Payments нет.
- **StoredFiles**(Path, ContentType, Size, Sha256, UploadedBy).
- **audit.audit_logs** (Id, At, UserId, Action, EntityType, EntityId, Before/After jsonb, Ip, UserAgent, CorrelationId). Только вставка: триггеры БД отклоняют UPDATE, DELETE и TRUNCATE для любого пользователя БД. Пароли и токены не записываются; логин при неудачном входе маскируется.

## 3. Индексы (основные)
- BookingRooms: GiST (RoomId, StayRange) — constraint; B-tree (BookingId).
- Bookings: (HotelId, Status, CheckIn), (GuestId), (Number).
- Payments: (BookingId), (GuestId, PaidAt), (Status), unique (Provider, ExternalId).
- Rooms: unique (HotelId, Number); (RoomTypeId, Status).
- HousekeepingTasks: (AssignedTo, Status), (RoomId, CreatedAt).
- WhatsAppMessages: (GuestId, CreatedAt), unique WaMessageId.
- AuditLogs: (EntityType, EntityId), (UserId, At).

## 4. Машина статусов
**Booking.** Начальные: `Pending` (веб в MVP, админ по выбору), `AwaitingPayment` (веб при онлайн-оплате), `Confirmed` (админ).
Pending→(AwaitingPayment|Confirmed|Cancelled|Expired); AwaitingPayment→(Confirmed|Expired|Cancelled); Confirmed→(CheckedIn|Cancelled|NoShow); CheckedIn→CheckedOut (в т.ч. досрочно — `StayRange` сокращается). Остальные переходы запрещены. `IsBlocking` в BookingRooms пересчитывается приложением в той же транзакции при каждом переходе: true для Pending, AwaitingPayment, Confirmed, CheckedIn.

**Room.** Хранимые: Available, Occupied, Cleaning, Maintenance, OutOfService.
Available→Occupied (check-in); Occupied→Cleaning (check-out); Cleaning→Available (задача Inspected либо Completed при `AutoReleaseRoomAfterCleaning`); Available|Cleaning→Maintenance|OutOfService (с причиной); Maintenance|OutOfService→Cleaning|Available (с причиной). Occupied→Maintenance запрещено (сначала выселение или переселение).
`Reserved` — вычисляемый статус отображения: хранимый Available и есть блокирующая бронь с CheckIn = сегодня. В RoomStatusHistory не пишется.

**Housekeeping:** Pending→Assigned→InProgress→Completed→(Inspected|Rejected); Rejected→Assigned (повторная уборка). Ошибочно созданную задачу в статусе Pending/Assigned администратор удаляет (soft delete `IsDeleted`, с записью в аудит); набор статусов — строго по ТЗ §3.3.

**Payment:** Pending→Processing→(Paid|Failed|Cancelled) — только канал Online; ручные платежи создаются в Paid. Paid→RefundPending (есть Approved, ещё не выплаченный возврат) → Refunded (возвращено всё) | PartiallyRefunded (часть) | Paid (возврат отклонён). PartiallyRefunded→RefundPending (следующий возврат). Статусы возврата выводятся из Refunds.

## 5. Транзакции и конкурентность
- Создание/изменение брони: одна транзакция (Bookings + BookingRooms + History + outbox); нарушение exclusion → 409.
- Для чтения-модификации-записи: `RowVersion`; для платежей — `SELECT … FOR UPDATE` либо unique-индексы + идемпотентность.
- Check-in/out — одна транзакция (Booking, Room, StatusHistory, HK-задача, outbox).

## 6. Soft delete
Guests, Rooms (архив), Expenses (void), Users (деактивация), HousekeepingTasks (только ошибочные Pending/Assigned). Платежи, транзакции, история, аудит — не удаляются никогда.

## 7. Миграции
Один `HmsDbContext` и одна цепочка EF Core migrations в проекте `Hms.Persistence`. Конфигурации сущностей лежат в `Infrastructure` своих модулей; у каждого модуля своя схема PostgreSQL (`identity`, `hotel`, `rooms`, `pricing`, `bookings`, `guests`, `housekeeping`, `finance`, `messaging`, `platform`, `audit`). Общий контекст нужен для транзакций, охватывающих несколько модулей (§5). Расширения (`btree_gist`) — в начальной миграции. Seed: роли, права, справочники.

## 8. Формулы показателей
Период P, N — число доступных номеров (не OutOfService), D — дней в P.
- **Начисленная выручка** = сумма стоимости проживания и услуг по ночам внутри P для броней (кроме Cancelled без штрафа, NoShow со штрафом включается).
- **Полученные платежи (Cash In)** = Σ `Paid` платежей по `PaidAt` ∈ P.
- **Возвраты** = Σ выплаченных возвратов в P.
- **Net Revenue** = Cash In − Возвраты.
- **Total Revenue** = начисленная выручка (отдельная метрика, не смешивается с Cash In).
- **Net Profit** = Net Revenue − Расходы (cash basis); начисленная прибыль отображается отдельно.
- **Occupancy** = проданные номеро-ночи / (N × D).
- **ADR** = выручка за проживание / проданные номеро-ночи.
- **RevPAR** = выручка за проживание / (N × D) = ADR × Occupancy.
- **Outstanding** = Σ max(0, начислено − оплачено + возвращено) по броням в статусах Confirmed, CheckedIn, CheckedOut, NoShow (совпадает с FIN-6; «оплачено» — Paid-платежи за вычетом сторно).
- **Именование на дашборде:** «Начисленная выручка» (Total Revenue), «Поступления» (Cash In), «Возвраты», «Чистые поступления» (Net Revenue), «Расходы», «Прибыль (по поступлениям)» (Net Profit) и отдельно «Прибыль (по начислению)» = Total Revenue − Расходы. Подписи и подсказки поясняют основу расчёта.
- **Avg Stay** = среднее число ночей по CheckedOut.

Налоги (НДС, налог с продаж) в расчётах не учитываются до решения вопроса о налоговом режиме.

## 9. Реализовано в Sprint 2 (схемы `hotel`, `rooms`, `platform`)
- **hotel.hotels** (Id, Name, Address, Phone E.164, Email, Description, TimeZone = Asia/Bishkek, Currency = KGS, xmin-версия). Гостиница одна; модули получают `HotelId` через `IHotelContext` (кэш, сбрасывается при изменении).
- **hotel.hotel_settings** (HotelId PK/FK, время заезда/выезда/No-Show, предоплата 0–100 %, удержания, флаги, `cancellation_policy` jsonb, правила, xmin-версия). Check: процент и удержания в допустимых пределах.
- **hotel.integration_settings** (HotelId, Provider MBank|Optima|WhatsApp|Email, IsEnabled, Mode, `public_config` jsonb без секретов, LastCheck*). Unique (HotelId, Provider).
- **rooms.amenities** (HotelId, Code unique в гостинице, Name, Icon, SortOrder) — 12 удобств по умолчанию добавляет сидер.
- **rooms.room_types** (HotelId, Name, Description, Capacity 1–20, BedsCount, BedType, BasePrice numeric(14,2) > 0, SortOrder, IsArchived, xmin) и **room_type_amenities** (M:N; удаление используемого удобства запрещено).
- **rooms.rooms** (HotelId, RoomTypeId, Number unique в гостинице, Floor, Capacity/BedsCount/BedType/PriceOverride — null = как у категории, Status Available|Occupied|Cleaning|Maintenance|OutOfService, IsArchived, xmin).
- **rooms.room_status_history** (RoomId, From/To, Source Manual|CheckIn|CheckOut|Housekeeping, Reason, ChangedBy, BookingId, ChangedAt) — номер с историей не удаляется, только архивируется.
- **rooms.room_photos** (RoomTypeId или RoomId — ровно одно, check; FileId → platform.stored_files; SortOrder).
- **platform.stored_files** (Path unique, ContentType, Size, Sha256, OriginalName, Visibility Public|Private, UploadedBy). Файл на диске — `{год}/{месяц}/{id}.{ext}` в каталоге `Storage:RootPath` (Docker volume `api-files`).

## 10. Реализовано в Sprint 3 (схемы `pricing`, `bookings`, `platform`)
- **pricing.room_rates** (HotelId, RoomTypeId, Name, DateFrom/DateTo — первая и последняя **ночь** включительно, DaysOfWeek — маска Пн=1…Вс=64 (выходные = Пт|Сб, т.е. ночи перед выходными днями), Price > 0, Priority 0–100, IsActive). Цена ночи: действующий тариф с наибольшим приоритетом (при равенстве — более новый), иначе `rooms.price_override`, иначе цена категории.
- **pricing.discounts** (HotelId, Name, Code — в верхнем регистре, unique в гостинице при наличии, Type Percent|Amount, Value > 0, ValidFrom/ValidTo — по дате заезда, MinNights — для посуточных, MinHours 0–24 — для почасовых (миграция `Sprint3_DiscountMinHours`), IsActive). Скидка применяется только к проживанию; процент округляется до тыйына (AwayFromZero); фиксированная сумма не больше стоимости проживания.
- **pricing.services** (HotelId, Name, Description, Price > 0, IsPerNight, IsPublic, IsActive). Удаления нет — только выключение (услуги упоминаются в бронях).
- **bookings.bookings** — по §2, с отличиями: до модуля Guests (Sprint 4) контакт гостя хранится в брони (`guest_name`, `guest_phone` E.164, `guest_email`), `guest_id` пока null — в Sprint 4 будет связь с карточкой гостя и перенос; `Number` = «ГГ-NNNNNN» из последовательности `bookings.booking_number_seq` (уникален без гонок); суммы `accommodation/discount/services/total_amount` numeric(14,2) и `price_snapshot` jsonb (цены по ночам, источник цены, скидка, услуги) фиксируются при создании; `PenaltyAmount`, `PaidAmount`, `ConfirmDueAt`, `PaymentDueAt`, `ActualCheckIn/OutAt` добавятся со своими спринтами (5, 6). Check: `check_out > check_in`, `adults >= 1`, `0 <= discount <= accommodation`.
- **bookings.booking_rooms** (BookingId, RoomId, RoomTypeId, CheckIn, CheckOut, IsBlocking, `stay_range daterange GENERATED ALWAYS AS (daterange(check_in, check_out, '[)')) STORED`). Ограничение **`ex_booking_rooms_no_overlap EXCLUDE USING gist (room_id WITH =, stay_range WITH &&) WHERE (is_blocking)`** создаётся SQL в миграции `Sprint3_PricingBookings`; нарушение (SQLSTATE 23P01) → `409 booking.room_unavailable`. `PricePerNight` не хранится в строке — цены по ночам в `price_snapshot`.
- **bookings.booking_services** (BookingId, ServiceId, Name и UnitPrice — снимок на момент заказа, Quantity > 0, PerNight, Total, AddedBy, AddedAt).
- **bookings.booking_history** (BookingId, EventType created|status_changed|details_changed, OldValues/NewValues jsonb, ChangedBy, ChangedAt, Comment) — только добавление.
- **platform.idempotency_keys** (Scope = операция + пользователь, Key, RequestHash SHA-256 тела, ResponseStatus, ResponseBody jsonb, IsProblem, CreatedAt, ExpiresAt = +24 ч). PK (Scope, Key). Захват ключа — `INSERT … ON CONFLICT`, поэтому параллельные повторы не выполняют операцию дважды; ответы 5xx не сохраняются (ключ освобождается).
- Модуль Rooms через `IRoomBookingQueries` (Bookings.Contracts): номер с бронями не удаляется (`409 room.has_bookings`), номер с действующими будущими бронями нельзя архивировать или перевести «вне продажи» (`409 room.has_upcoming_bookings`); статус отображения `Reserved` — свободный номер с заездом сегодня.

## 11. Почасовые брони и продление (доработка Sprint 3, миграция `Sprint3_HourlyBookings`)
Решения заказчика 2026-11-20: основной поток — почасовые брони; посуточные остаются; продление брони.
- **hotel.hotel_settings**: `hourly_min_hours` (2), `hourly_max_hours` (12), `cleaning_buffer_minutes` (30). Шаг начала — 1 час (правило, не настройка).
- **rooms.room_types.hourly_price** (null — по часам не продаётся), **rooms.rooms.hourly_price_override**.
- **pricing.room_rates.unit** `Night|Hour`: тариф «за ночь» влияет только на посуточные брони, «за час» — только на почасовые (час относится к дате и дню недели своего начала).
- **bookings.bookings**: `kind` `Nightly|Hourly`, `starts_at`, `ends_at` (UTC), `hours` (почасовые, с продлениями), `extensions_amount`. `check_in/check_out` — даты по времени гостиницы (у почасовой — дата начала и окончания, могут совпадать).
- **bookings.booking_rooms**: `starts_at`, `ends_at`, `blocked_until` = выезд + уборка. Для посуточной брони время — часы заезда/выезда из настроек на момент создания; уборка — не дольше промежутка между выездом и заездом (чтобы посуточные брони «выезд в день заезда» не конфликтовали). Столбец `occupancy tstzrange GENERATED ALWAYS AS (tstzrange(starts_at, blocked_until, '[)')) STORED` и **`ex_booking_rooms_no_overlap EXCLUDE USING gist (room_id WITH =, occupancy WITH &&) WHERE (is_blocking)`** заменили прежние `stay_range daterange` и ограничение по датам. Посуточные и почасовые брони одного номера проверяются одним ограничением. Существующие брони перенесены миграцией (время заезда/выезда из настроек).
- **bookings.booking_extensions** (BookingId, Unit Hourly|Nightly, Quantity, PreviousEndsAt, NewEndsAt, Amount, PriceSnapshot jsonb, CreatedBy, CreatedAt). Продление — только для `Confirmed`/`CheckedIn`; добавленное время по ценам на момент продления, без скидки и услуг; исходный `price_snapshot` не меняется; `total_amount += amount`. Конфликт со следующей бронью (с учётом уборки) → `409 booking.extension_conflict`; двойное нажатие — `version` (xmin) → `409 concurrency.conflict`.

## 12. Реализовано в Sprint 4 (схема `guests`, миграция `Sprint4_Guests`)
- **guests.guests** (HotelId, UserId — аккаунт на сайте, unique при наличии; FullName, Phone/WhatsAppPhone E.164, Email в нижнем регистре, BirthDate, Notes — только для персонала, Source Admin|Booking|Portal, CreatedAt, UpdatedAt, AnonymizedAt, xmin). Документы не хранятся (решение заказчика). Уникальности телефона/email в БД нет: дубли показываются администратору (`409 guest.duplicate` с id найденной карточки), слияние — P1. Индексы (HotelId, Phone), (HotelId, Email).
- **guests.guest_consents** (GuestId, Type PersonalData|WhatsApp, Granted, Source Portal|FrontDesk|Phone|WhatsApp|Written, RecordedBy, RecordedAt) — только добавление; текущее состояние = последняя запись.
- **bookings.bookings.guest_id** заполняется при создании брони: выбранный гость или найденный по телефону (затем email), иначе новая карточка. `guest_name/phone/email` в брони — снимок контакта на момент брони. Межмодульного FK нет (модули связаны через контракты).
- **Перенос данных** миграцией: аккаунты гостей (identity.users, user_type = Guest) → карточки; согласия, записанные при регистрации в аудит (Sprint 1) → guest_consents; брони → привязка к карточке по телефону/email или новая карточка (ключ: телефон, иначе email, иначе имя).
- **Обезличивание** (`guest.anonymize`, только владелец): в одной транзакции стираются ФИО, контакты, дата рождения, заметки карточки, снимок контакта во всех бронях гостя и поля `guestName/guestPhone/guestEmail` в `booking_history`. Цифры по броням и история согласий остаются. Гость с аккаунтом на сайте пока не обезличивается (вместе с удалением аккаунта — Sprint 8).
- Цифры карточки считаются по броням: броней, посещений (CheckedIn/CheckedOut), сумма действующих и завершённых броней; «Общая сумма расходов» по оплатам — с модулем Finance (Sprint 6).

## 13. Реализовано в Sprint 5 (миграция `Sprint5_CalendarRules`)
- **bookings.bookings**: `hold_until` (до какого момента неподтверждённая бронь держит номер; индекс по непустым значениям для фоновой задачи), `penalty_amount numeric(14,2) ≥ 0`, `penalty_kind` (LateCancel|NoShow), `penalty_waived` (штраф снят сотрудником с правом). Штраф только начисляется: приём оплаты и долг по брони — модуль Finance (Sprint 6).
- **hotel.hotel_settings.auto_no_show_enabled** (по умолчанию false): пока нет заселения (Sprint 7), система не знает, пришёл ли гость, поэтому автоматическая неявка выключена; неявку отмечают вручную.
- **Удержание** `hold_until` задаётся при создании неподтверждённой брони: почасовая — `PendingHoldMinutes`, посуточная — `PendingHoldHours`, но не позже начала брони (если оно впереди). Подтверждение и отмена очищают его. Миграция проставила срок существующим неподтверждённым броням от момента их создания.
- **Перенос** меняет строку `booking_rooms` (номер, `starts_at`, `blocked_until`) в той же транзакции; пересечения по-прежнему исключает ограничение EXCLUDE — гонка двух переносов на одно время даёт `409`.
- **Штраф** = средняя цена часа (ночи) брони после скидки, с продлениями × число часов (ночей) по правилу, не больше стоимости проживания; услуги в штраф не входят. Неподтверждённая бронь отменяется без штрафа.

## 14. Реализовано в Sprint 6 (схема `finance`, миграция `Sprint6_Finance`)
- **finance.payments** (HotelId, BookingId?, GuestId?, Type Prepayment|Stay|Service|Penalty|Other|Reversal, Method Cash|BankTransfer|CardPos|MBank|Optima, Channel Manual|Online, Amount numeric(14,2) — у сторно отрицательная, Status, ReversalOfPaymentId? unique, RefundedAmount, PaidAt, RegisteredBy, Comment, CreatedAt, xmin). Отличие от §2: добавлен тип `Penalty` (оплата штрафа отмены/неявки), статус `Reversed`; `Provider`/`ExternalId` — у операций в журнале (появятся с эквайрингом). Check: сторно < 0, остальное > 0; 0 ≤ RefundedAmount ≤ |Amount|.
- **Неизменяемость на уровне БД**: триггер `payments_immutable` запрещает DELETE и изменение всех полей платежа, кроме статуса, выплаченной суммы возвратов и версии; `payment_transactions_append_only` — только вставка. TRUNCATE не блокируется (приложение его не выполняет; нужен скрипту сброса локальной базы).
- **finance.payment_transactions** (PaymentId, Kind Charge|Refund|Reversal|StatusChange, Amount, Status, Provider?, ProviderTxnId? — unique с Kind, CreatedBy, Comment, OccurredAt) — журнал каждого изменения платежа.
- **finance.refunds** (PaymentId FK, BookingId, Amount > 0, Reason, Method, Status Requested|Approved|Rejected|Paid, RequestedBy/At, DecidedBy/At, DecisionComment, PaidBy/At, xmin) — источник истины по возвратам; Σ не отклонённых ≤ суммы платежа — проверка в приложении под advisory-lock по платежу.
- **finance.expenses** (CategoryId FK, Amount > 0, Date, Method, Supplier, Description, CreatedBy, IsVoided, VoidedBy/At, VoidReason, xmin) и **finance.expense_categories** (Code unique в гостинице, Name, SortOrder, IsActive) — категории по умолчанию создаёт сидер; налоговых категорий нет (решение заказчика: налоги и фискализацию система не ведёт).
- **Баланс брони** = к оплате − Σ(Amount − RefundedAmount) по полученным платежам (Paid, RefundPending, PartiallyRefunded, Refunded, Reversed + отрицательное сторно). К оплате берётся из Bookings через `IBookingCharges` (стоимость; у отменённой/неявки — штраф). Денормализованного `PaidAmount` в брони нет.
- **bookings.bookings.rescheduled_at** (миграция `Sprint6_BookingRescheduledAt`): продлённую бронь теперь можно переносить — её время (с продлениями) пересчитывается одной ценой; `extensions_amount` обнуляется, прежние продления остаются в `booking_extensions` как история, но в стоимость не входят (в брони показываются продления после `rescheduled_at`).
- **Время уборки** (миграция `Sprint6_CleaningPerCategory`): `hotel.hotel_settings.cleaning_buffer_minutes` — после почасовой брони, `cleaning_buffer_nightly_minutes` — после посуточной (для существующей гостиницы взято прежнее общее значение); `rooms.room_types.cleaning_hourly_minutes` / `cleaning_nightly_minutes` — своё у категории (null — как у гостиницы). Порядок: категория → гостиница; после посуточной — не дольше промежутка между часом выезда и часом заезда. `blocked_until` фиксируется при создании, переносе и продлении брони: изменение настроек не сдвигает уже созданные брони.
- **Номер брони** (миграция `Sprint6_BookingNumbers`, решение заказчика 2026-11-21): «Ч-2111-07» — вид (Ч — почасовая, С — посуточная) + день создания по времени гостиницы (ДДММ) + номер за день (от 01; с сотой — трёхзначный). Счётчик — `bookings.booking_number_counters (hotel_id, day, last)`, номер выдаётся одной командой `INSERT … ON CONFLICT DO UPDATE … RETURNING` (без гонок, пропуски допустимы); счётчик общий для обоих видов. Год в номере не пишется — `bookings.number_year`; уникальность — `(hotel_id, number_year, number)`. Последовательность `booking_number_seq` удалена; брони, созданные раньше, сохраняют номера «26-000042».

## 15. Реализовано в Sprint 7 (схема `housekeeping`, миграция `Sprint7_FrontDeskHousekeeping`)
- **bookings.bookings**: `actual_check_in_at`, `actual_check_out_at` (фактические заселение и выселение), `early_departure_credit numeric(14,2)` — перерасчёт при досрочном выезде, уже вычтенный из `total_amount`. При досрочном выезде `ends_at`/`blocked_until` сокращаются до фактического выезда + уборка (после выезда бронь номер не блокирует — `is_blocking = false`). Если гость уехал до начала брони, время не сокращается (check `ends_at > starts_at`).
- **housekeeping.tasks** (HotelId, RoomId, BookingId?, Origin CheckOut|Manual, Status Pending|Assigned|InProgress|Completed|Inspected|Rejected, Priority Normal|High, AssignedTo?, DueAt?, Notes, CreatedBy?, CreatedAt, StartedAt, CompletedAt, InspectedBy?, InspectedAt, RejectReason, IsDeleted, xmin). Отличие от §2: вместо таблицы назначений — поле `AssignedTo` (одна задача — один уборщик); фото — P1. Мягкое удаление (query filter), только Pending/Assigned. Индексы: (assigned_to, status), (room_id, created_at), (hotel_id, status), booking_id.
- **housekeeping.task_checklist_items** (TaskId FK cascade, Position, Text, IsDone, DoneAt) — копия пунктов шаблона на момент создания задачи: правка шаблона не меняет начатые задачи.
- **housekeeping.checklist_templates** (HotelId, Name, IsDefault, Items `text[]`, xmin) — один шаблон по умолчанию на гостиницу (уникальный частичный индекс); создаёт сидер, пункты правит администратор.
- **housekeeping.maintenance_requests** (RoomId, TaskId?, Type Defect|Damage, Severity Low|Normal|High|Critical, Description, Status New|InProgress|Resolved|Rejected, ReportedBy, CreatedAt, ResolvedBy/At, ResolutionNote, xmin).
- Заселение/выселение — одна транзакция (один `HmsDbContext`): бронь, `rooms.rooms.status` + `room_status_history` (через `IRoomStatusChanger`, Rooms.Contracts), задача уборки (через `IHousekeepingTasks`, Housekeeping.Contracts), аудит.

## 16. Sprint 8 (без новых таблиц)
- Схема не менялась: брони с сайта — `bookings.bookings.source = 'Web'`, `guest_id` — карточка гостя, связанная с аккаунтом (`guests.guests.user_id`).
- Удаление аккаунта гостем: карточка обезличивается и отвязывается от аккаунта (`user_id = null`), брони обезличиваются; строка `identity.users` удаляется (роли, права, refresh-токены — каскадом). Согласия и платежи остаются (платежи не содержат ПД).

## 17. Реализовано в Sprint 9 (модуль Reporting, без своих таблиц)
Отчёты читают данные других модулей только через контракты: `IBookingFacts` (Bookings), `IFinanceFacts` (Finance), `IHousekeepingFacts` (Housekeeping), `IRoomDirectory`, `IGuestDirectory.GetManyAsync`, `IUserDirectory`. Расчёт — чистые функции `Hms.Modules.Reporting.Domain.Kpi` (юнит-тесты на эталонных данных).

**Уточнение формул §8 под почасовой поток (Sprint 9):**
- **Период** — даты гостиницы включительно, границы суток по Asia/Bishkek; не больше 366 дней.
- **Начисленная выручка** — стоимость брони (проживание − скидка + услуги + продления − перерасчёт за досрочный выезд) распределяется по времени: на период приходится доля, пропорциональная пересечению проживания с периодом (почасовые — по времени, посуточные — по ночам). Учитываются Confirmed, CheckedIn, CheckedOut; Pending и Expired — нет. Отменённые и неявки — только начисленным штрафом на дату отмены (неявки — на начало брони).
- **Поступления** — платежи с датой оплаты в периоде, аннулирования — отрицательной суммой на дату аннулирования; **возвраты** — выплаченные в периоде. Чистые поступления, прибыль по поступлениям и по начислению — как в §8.
- **Загрузка** — по времени: занятые номеро-часы (проданные брони, пересечение с периодом) / (доступные номера × часы периода). Подходит и для почасовых, и для посуточных броней.
- **Почасовые:** проданные номеро-часы; **средняя цена часа** = выручка за проживание почасовых / часы; **выручка на номер-час** = выручка за проживание всех броней / (номера × часы); **средняя почасовая бронь** — по завершённым (CheckedOut) в периоде; **оборот номера в день** = состоявшиеся брони с началом в периоде / (номера × дни).
- **Посуточные (ADR, RevPAR — только по ним):** номеро-ночи — ночи посуточных броней внутри периода; ADR = выручка за проживание посуточных / номеро-ночи; RevPAR = та же выручка / (номера × дни).
- **Брони за период** — с началом проживания в периоде (истёкшие не считаются); отмены и неявки — из них.
- **Доступные номера** — текущие номера не в архиве и не «вне продажи» (истории фонда нет — изменение фонда в прошлом не учитывается).
- **Долги** на дашборде — на текущий момент (не за период), как в «Оплаты → Долги»: начатые брони, где оплачено меньше начисленного.
- **Фильтр по категории** сужает номера, брони, платежи и возвраты по броням этой категории; расходы гостиницы на категории не делятся — с этим фильтром не учитываются (отчёт об этом пишет).
- Налоги не учитываются (решение заказчика: система не ведёт налоги и фискализацию).
- **Время внутри дня** (2026-11-22): период может начинаться и заканчиваться в целый час (`fromHour`/`toHour`); первый и последний день обрезаются, остальные — сутки целиком. Выручка, загрузка, платежи и брони считаются по пересечению с этим интервалом (почасовые — по времени). Посуточная бронь делится по ночам: ночь, приходящаяся на частично попавший день, учитывается целиком. Расходы — за целые дни.

## 18. Переселение заселённого гостя (2026-11-22, миграция `Sprint9_RoomMove`)
- **bookings.bookings**: `room_change_amount numeric(14,2)` — изменение стоимости при переселении с пересчётом (может быть отрицательным), уже учтено в `total_amount`; входит в начисленную выручку отчётов.
- **bookings.booking_rooms** — у брони теперь может быть несколько строк-отрезков: прежний номер `[starts_at, момент переселения)` с уборкой после него и новый номер `[момент переселения, ends_at)`. Текущий номер брони — отрезок с самым поздним `starts_at`; продление и досрочный выезд меняют только его. Exclusion constraint по-прежнему проверяет каждый отрезок. Отчёт по номерам считает часы и выручку каждого номера по его отрезку (выручка делится пропорционально времени).
