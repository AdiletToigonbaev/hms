# ER-диаграмма

**Статус:** предложена архитектором, ожидает согласования заказчиком (ТЗ §14: «сначала подготовить ER-диаграмму и согласовать её»). Описание полей и обоснование отклонений от списка таблиц ТЗ: `database-design.md`.

Диаграмма разбита по модулям для читаемости. Рендер: любой Mermaid-совместимый просмотрщик (GitHub, VS Code). Во всех бизнес-таблицах есть `HotelId → Hotels` (на диаграммах не показано, кроме модуля Hotel).

## 1. Identity и Hotel
```mermaid
erDiagram
  Hotels ||--|| HotelSettings : has
  Hotels ||--o{ IntegrationSettings : configures
  Users ||--o{ UserRoles : has
  Roles ||--o{ UserRoles : has
  Roles ||--o{ RolePermissions : grants
  Permissions ||--o{ RolePermissions : in
  Users ||--o{ UserPermissions : override
  Permissions ||--o{ UserPermissions : in
  Users ||--o{ RefreshTokens : owns
```

## 2. Номера и тарифы
```mermaid
erDiagram
  Hotels ||--o{ RoomTypes : has
  RoomTypes ||--o{ Rooms : contains
  RoomTypes ||--o{ RoomRates : priced
  RoomTypes }o--o{ Amenities : "via RoomTypeAmenities"
  RoomTypes ||--o{ RoomPhotos : "category photos"
  Rooms ||--o{ RoomPhotos : "room photos"
  StoredFiles ||--o{ RoomPhotos : file
  Rooms ||--o{ RoomStatusHistory : logs
  Hotels ||--o{ Services : offers
  Hotels ||--o{ Discounts : defines
```

## 3. Гости и бронирования
```mermaid
erDiagram
  Users |o--o| Guests : "account"
  Guests ||--o{ GuestDocuments : has
  Guests ||--o{ GuestConsents : gives
  Guests ||--o{ Bookings : makes
  Bookings ||--|{ BookingRooms : reserves
  Rooms ||--o{ BookingRooms : "exclude overlap"
  RoomTypes ||--o{ BookingRooms : "type at booking"
  Bookings ||--o{ BookingGuests : includes
  Guests ||--o{ BookingGuests : participates
  Bookings ||--o{ BookingServices : adds
  Services ||--o{ BookingServices : of
  Bookings ||--o{ BookingHistory : logs
  Discounts |o--o{ Bookings : "applied (DiscountId)"
```

## 4. Финансы
```mermaid
erDiagram
  Bookings |o--o{ Payments : paid_by
  Guests ||--o{ Payments : pays
  Payments |o--o| Payments : "reversal of"
  Payments ||--o{ PaymentTransactions : events
  Payments ||--o{ Refunds : refunded_by
  Bookings ||--o{ Refunds : for
  ExpenseCategories ||--o{ Expenses : groups
  StoredFiles |o--o{ Expenses : attachment
  WebhookEvents }o..o{ PaymentTransactions : "dedup source"
```

## 5. Уборка
```mermaid
erDiagram
  Rooms ||--o{ HousekeepingTasks : cleaned
  Bookings |o--o{ HousekeepingTasks : triggers
  Users |o--o{ HousekeepingTasks : assigned
  HousekeepingTasks ||--o{ HousekeepingChecklistItems : contains
  ChecklistTemplates ||--o{ ChecklistTemplateItems : defines
  RoomTypes |o--o{ ChecklistTemplates : for
  Rooms ||--o{ MaintenanceRequests : reported
  HousekeepingTasks |o--o{ MaintenanceRequests : found_in
```

## 6. Сообщения и платформа
```mermaid
erDiagram
  Guests ||--o{ WhatsAppMessages : conversation
  NotificationTemplates |o--o{ WhatsAppMessages : uses
  Bookings |o--o{ WhatsAppMessages : about
  OutboxMessages |o..o| WhatsAppMessages : delivers
  Users |o--o{ AuditLogs : acts
```
`OutboxMessages`, `WebhookEvents`, `IdempotencyKeys`, `StoredFiles` — технические таблицы модуля Platform; связь с бизнес-сущностями логическая (через payload/ключи), без внешних ключей.

## Критичные ограничения
- `BookingRooms`: `EXCLUDE USING gist (RoomId WITH =, StayRange WITH &&) WHERE (IsBlocking)`.
- `Bookings`: check `CheckOut > CheckIn`, `AdultsCount ≥ 1`.
- `Payments`: unique `(Provider, ExternalId)` where not null; unique `(ReversalOfPaymentId)` where not null; check `Amount > 0`.
- `PaymentTransactions`: unique `(Provider, ProviderTxnId, Kind)` where `ProviderTxnId` not null.
- `WebhookEvents`: unique `(Source, DedupKey)`.
- `OutboxMessages`: unique `DedupKey`. `IdempotencyKeys`: unique `(Scope, Key)`.
- `Rooms`: unique `(HotelId, Number)`. `IntegrationSettings`: unique `(HotelId, Provider)`.
- `RoomPhotos`: check — заполнено ровно одно из `RoomTypeId`/`RoomId`.
