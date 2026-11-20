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
- **GuestDocuments**(GuestId, DocType, NumberEncrypted, IssuedBy, ValidTo, LegalBasis) — ограниченный доступ.
- **GuestConsents**(GuestId, Type: PersonalData|WhatsApp, GrantedAt, RevokedAt, Source).

### Bookings
- **Bookings**(Number unique human-readable, GuestId, Status, Source Web|Admin|WhatsApp, CheckIn date, CheckOut date, AdultsCount, ChildrenCount, DiscountId null, TotalAmount, DiscountAmount, ServicesAmount, PenaltyAmount, PaidAmount (денорм., пересчёт из платежей), PriceSnapshot jsonb, ConfirmDueAt null, PaymentDueAt null, CancelledAt, CancelReason, ActualCheckInAt, ActualCheckOutAt, Comment, RowVersion).
- **BookingRooms**(BookingId, RoomId, RoomTypeId, PricePerNight, StayRange daterange, IsBlocking bool). **EXCLUDE USING gist (RoomId WITH =, StayRange WITH &&) WHERE (IsBlocking)**. IsBlocking = статус брони ∈ {Pending, AwaitingPayment, Confirmed, CheckedIn}.
- **BookingGuests**(BookingId, GuestId, IsPrimary).
- **BookingServices**(BookingId, ServiceId, Qty, UnitPrice, AddedAt, AddedBy).
- **BookingHistory**(BookingId, EventType, OldValues jsonb, NewValues jsonb, ChangedBy, ChangedAt, Comment).
- Check: `CheckOut > CheckIn`, `Adults ≥ 1`.

### Finance
- **Payments**(BookingId null, GuestId, Type Prepayment|Stay|Service|Other|Reversal, Method Cash|BankTransfer|CardPos|MBank|Optima, Channel Manual|Online, Amount, Status, Provider null, ExternalId null, ReversalOfPaymentId null, RefundedAmount (денорм. из Refunds), FiscalReceiptNo null, PaidAt, RegisteredBy null (ручной), Comment, RowVersion). Unique (Provider, ExternalId) where not null. Неизменяемы все поля, кроме Status/RefundedAmount/RowVersion; каждое изменение статуса пишет строку в PaymentTransactions. Ручной платёж создаётся сразу в `Paid`. Unique (ReversalOfPaymentId) where not null — сторно не более одного.
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
- **pricing.discounts** (HotelId, Name, Code — в верхнем регистре, unique в гостинице при наличии, Type Percent|Amount, Value > 0, ValidFrom/ValidTo — по дате заезда, MinNights, IsActive). Скидка применяется только к проживанию; процент округляется до тыйына (AwayFromZero); фиксированная сумма не больше стоимости проживания.
- **pricing.services** (HotelId, Name, Description, Price > 0, IsPerNight, IsPublic, IsActive). Удаления нет — только выключение (услуги упоминаются в бронях).
- **bookings.bookings** — по §2, с отличиями: до модуля Guests (Sprint 4) контакт гостя хранится в брони (`guest_name`, `guest_phone` E.164, `guest_email`), `guest_id` пока null — в Sprint 4 будет связь с карточкой гостя и перенос; `Number` = «ГГ-NNNNNN» из последовательности `bookings.booking_number_seq` (уникален без гонок); суммы `accommodation/discount/services/total_amount` numeric(14,2) и `price_snapshot` jsonb (цены по ночам, источник цены, скидка, услуги) фиксируются при создании; `PenaltyAmount`, `PaidAmount`, `ConfirmDueAt`, `PaymentDueAt`, `ActualCheckIn/OutAt` добавятся со своими спринтами (5, 6). Check: `check_out > check_in`, `adults >= 1`, `0 <= discount <= accommodation`.
- **bookings.booking_rooms** (BookingId, RoomId, RoomTypeId, CheckIn, CheckOut, IsBlocking, `stay_range daterange GENERATED ALWAYS AS (daterange(check_in, check_out, '[)')) STORED`). Ограничение **`ex_booking_rooms_no_overlap EXCLUDE USING gist (room_id WITH =, stay_range WITH &&) WHERE (is_blocking)`** создаётся SQL в миграции `Sprint3_PricingBookings`; нарушение (SQLSTATE 23P01) → `409 booking.room_unavailable`. `PricePerNight` не хранится в строке — цены по ночам в `price_snapshot`.
- **bookings.booking_services** (BookingId, ServiceId, Name и UnitPrice — снимок на момент заказа, Quantity > 0, PerNight, Total, AddedBy, AddedAt).
- **bookings.booking_history** (BookingId, EventType created|status_changed|details_changed, OldValues/NewValues jsonb, ChangedBy, ChangedAt, Comment) — только добавление.
- **platform.idempotency_keys** (Scope = операция + пользователь, Key, RequestHash SHA-256 тела, ResponseStatus, ResponseBody jsonb, IsProblem, CreatedAt, ExpiresAt = +24 ч). PK (Scope, Key). Захват ключа — `INSERT … ON CONFLICT`, поэтому параллельные повторы не выполняют операцию дважды; ответы 5xx не сохраняются (ключ освобождается).
- Модуль Rooms через `IRoomBookingQueries` (Bookings.Contracts): номер с бронями не удаляется (`409 room.has_bookings`), номер с действующими будущими бронями нельзя архивировать или перевести «вне продажи» (`409 room.has_upcoming_bookings`); статус отображения `Reserved` — свободный номер с заездом сегодня.
