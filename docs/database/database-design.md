# Проектирование базы данных (PostgreSQL)

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
| Добавлена `MessageOutbox` | надёжная отправка WhatsApp (см. архитектуру) |
| Добавлены `ExpenseCategories`, `StoredFiles` | расходы по категориям; единое хранение вложений/фото |
| `BookingRooms` содержит `StayRange` | exclusion constraint против пересечений |
| `PaymentTransactions` отдельно от `Payments` | `Payment` — бизнес-намерение, `Transaction` — каждая попытка/событие у провайдера |
| Пользовательская таблица `Users` = Identity (`AspNetUsers`) + `StaffProfiles`/`Guests` связь | не дублировать Identity; персонал и гость — разные профили |

## 2. Таблицы по модулям

### Identity
- **Users** (Identity): Id, Email, PhoneNumber, PasswordHash, UserType (Staff|Guest), IsActive, FullName, LockoutEnd…
- **Roles** (Owner, Administrator, Housekeeper, Guest), **UserRoles**.
- **Permissions** (Code unique, Description). **RolePermissions**(RoleId, PermissionId). **UserPermissions**(UserId, PermissionId, IsGranted, LimitAmount null).
- **RefreshTokens**(UserId, TokenHash, ExpiresAt, RevokedAt, ReplacedBy, UserAgent).

### Hotel
- **Hotels**(Name, Address, Phone, TimeZone, Currency).
- **HotelSettings**(HotelId PK, CheckInTime, CheckOutTime, OnlinePaymentEnabled, PrepaymentPercent, PaymentHoldMinutes, AutoConfirmWebBookings, AutoReleaseRoomAfterCleaning, CancellationPolicy jsonb, Rules text, Locale…).

### Rooms
- **RoomTypes**(Name, Description, Capacity, BedsCount, BedType, BasePrice, IsActive).
- **Rooms**(RoomTypeId, Number, Floor, Capacity, BedsCount, BedType, PriceOverride null, Description, Status, IsArchived). Unique (HotelId, Number).
- **Amenities**(Code, Name), **RoomTypeAmenities**, **RoomPhotos**(RoomTypeId|RoomId, FileId, SortOrder).
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
- **Bookings**(Number unique human-readable, GuestId, Status, Source Web|Admin, CheckIn date, CheckOut date, AdultsCount, ChildrenCount, TotalAmount, DiscountAmount, ServicesAmount, PaidAmount (денорм., пересчёт из платежей), PriceSnapshot jsonb, PaymentDueAt, CancelledAt, CancelReason, ActualCheckInAt, ActualCheckOutAt, Comment, RowVersion).
- **BookingRooms**(BookingId, RoomId, RoomTypeId, PricePerNight, StayRange daterange, IsBlocking bool). **EXCLUDE USING gist (RoomId WITH =, StayRange WITH &&) WHERE (IsBlocking)**. IsBlocking = статус брони ∈ {Pending, AwaitingPayment, Confirmed, CheckedIn}.
- **BookingGuests**(BookingId, GuestId, IsPrimary).
- **BookingServices**(BookingId, ServiceId, Qty, UnitPrice, AddedAt, AddedBy).
- **BookingHistory**(BookingId, EventType, OldValues jsonb, NewValues jsonb, ChangedBy, ChangedAt, Comment).
- Check: `CheckOut > CheckIn`, `Adults ≥ 1`.

### Finance
- **Payments**(BookingId null, GuestId, Type Stay|Prepayment|Service|Other, Method Cash|BankTransfer|Card|MBank|Optima, Amount, Status, Provider null, ExternalId null, IdempotencyKey, PaidAt, RegisteredBy null (ручной), Comment). Unique (Provider, ExternalId) where not null.
- **PaymentTransactions**(PaymentId, Kind Charge|Refund|Reversal, Amount, Status, ProviderTxnId, RawRequest/Response jsonb без карточных данных, OccurredAt). Append-only. Unique (Provider, ProviderTxnId, Kind).
- **Refunds**(PaymentId, BookingId, Amount, Reason, Status Requested|Approved|Rejected|Paid, RequestedBy, ApprovedBy, DecidedAt).
- **Expenses**(CategoryId, Amount, Date, Supplier, Description, FileId null, CreatedBy, IsVoided).
- **ExpenseCategories**.
- Check: `Amount > 0`; отрицательные суммы не используются, направление задаёт тип.

### Housekeeping
- **ChecklistTemplates**(RoomTypeId null, Name) / **ChecklistTemplateItems**(TemplateId, Text, SortOrder).
- **HousekeepingTasks**(RoomId, BookingId null, Status, Priority, AssignedTo null, CreatedBy|Auto, StartedAt, CompletedAt, InspectedBy, InspectedAt, RejectReason, Notes).
- **HousekeepingChecklistItems**(TaskId, Text, IsDone, DoneAt).
- **MaintenanceRequests**(RoomId, ReportedBy, TaskId null, Type Defect|Damage, Description, Severity, Status, ResolvedAt, FileIds).

### Messaging
- **NotificationTemplates**(Code, Language, WaTemplateName, WaTemplateStatus, Body, Variables jsonb, IsActive).
- **WhatsAppMessages**(GuestId, Direction In|Out, WaMessageId, TemplateCode null, Body, Status Queued|Sent|Delivered|Read|Failed, Error, Timestamps, SentBy null, BookingId null).
- **MessageOutbox**(Type, Payload jsonb, Attempts, NextAttemptAt, Status, DedupKey unique).

### Platform / Audit
- **WebhookEvents**(Source MBank|Optima|WhatsApp, EventId/DedupKey, Payload jsonb, SignatureValid, ReceivedAt, ProcessedAt, Error). Unique (Source, DedupKey).
- **IdempotencyKeys**(Key, Scope, RequestHash, ResponseStatus, ResponseBody, ExpiresAt).
- **StoredFiles**(Path, ContentType, Size, Sha256, UploadedBy).
- **AuditLogs**(At, UserId, Action, EntityType, EntityId, Before/After jsonb, Ip, UserAgent, CorrelationId).

## 3. Индексы (основные)
- BookingRooms: GiST (RoomId, StayRange) — constraint; B-tree (BookingId).
- Bookings: (HotelId, Status, CheckIn), (GuestId), (Number).
- Payments: (BookingId), (GuestId, PaidAt), (Status), unique (Provider, ExternalId).
- Rooms: unique (HotelId, Number); (RoomTypeId, Status).
- HousekeepingTasks: (AssignedTo, Status), (RoomId, CreatedAt).
- WhatsAppMessages: (GuestId, CreatedAt), unique WaMessageId.
- AuditLogs: (EntityType, EntityId), (UserId, At).

## 4. Машина статусов
**Booking:** Pending→(AwaitingPayment|Confirmed|Cancelled|Expired); AwaitingPayment→(Confirmed|Expired|Cancelled); Confirmed→(CheckedIn|Cancelled|NoShow); CheckedIn→CheckedOut. Остальные переходы запрещены.
**Room:** Available↔Reserved (по ближайшей броне, расчётно), Available→Occupied (check-in), Occupied→Cleaning (check-out), Cleaning→Available (Inspected либо авто по настройке), любой→Maintenance/OutOfService (с причиной) и обратно через Available/Cleaning.
**Housekeeping:** Pending→Assigned→InProgress→Completed→(Inspected|Rejected→Assigned).
**Payment:** Pending→Processing→(Paid|Failed|Cancelled); Paid→RefundPending→(Refunded|PartiallyRefunded|Paid при отказе).

## 5. Транзакции и конкурентность
- Создание/изменение брони: одна транзакция (Bookings + BookingRooms + History + outbox); нарушение exclusion → 409.
- Для чтения-модификации-записи: `RowVersion`; для платежей — `SELECT … FOR UPDATE` либо unique-индексы + идемпотентность.
- Check-in/out — одна транзакция (Booking, Room, StatusHistory, HK-задача, outbox).

## 6. Soft delete
Guests, Rooms (архив), Expenses (void), Users (деактивация). Платежи, транзакции, история, аудит — не удаляются никогда.

## 7. Миграции
EF Core migrations в `Infrastructure` каждого модуля с общим контекстом по схемам (`identity`, `rooms`, `bookings`, `finance` …) — схемы PostgreSQL на модуль для изоляции. Расширения (`btree_gist`) — в начальной миграции. Seed: роли, права, справочники.

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
- **Outstanding** = Σ (начислено − оплачено) по активным и завершённым броням с долгом.
- **Avg Stay** = среднее число ночей по CheckedOut.

Налоги (НДС, налог с продаж) в расчётах не учитываются до решения вопроса о налоговом режиме.
