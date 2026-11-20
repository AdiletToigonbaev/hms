# ER-диаграмма

Диаграмма разбита по модулям для читаемости. Рендер: любой Mermaid-совместимый просмотрщик (GitHub, VS Code).

## 1. Identity и Hotel
```mermaid
erDiagram
  Hotels ||--|| HotelSettings : has
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
  RoomTypes ||--o{ RoomPhotos : shows
  Rooms ||--o{ RoomStatusHistory : logs
  StoredFiles ||--o{ RoomPhotos : file
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
  Bookings ||--o{ BookingGuests : includes
  Guests ||--o{ BookingGuests : participates
  Bookings ||--o{ BookingServices : adds
  Services ||--o{ BookingServices : of
  Bookings ||--o{ BookingHistory : logs
  Discounts ||--o{ Bookings : applied
```

## 4. Финансы
```mermaid
erDiagram
  Bookings ||--o{ Payments : paid_by
  Guests ||--o{ Payments : pays
  Payments ||--o{ PaymentTransactions : events
  Payments ||--o{ Refunds : refunded_by
  ExpenseCategories ||--o{ Expenses : groups
  StoredFiles ||--o{ Expenses : attachment
  WebhookEvents }o..o{ PaymentTransactions : "dedup source"
```

## 5. Уборка
```mermaid
erDiagram
  Rooms ||--o{ HousekeepingTasks : cleaned
  Bookings |o--o{ HousekeepingTasks : triggers
  Users ||--o{ HousekeepingTasks : assigned
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
  NotificationTemplates ||--o{ WhatsAppMessages : uses
  MessageOutbox ||--o| WhatsAppMessages : delivers
  Users ||--o{ AuditLogs : acts
```

## Критичные ограничения
- `BookingRooms`: `EXCLUDE USING gist (RoomId WITH =, StayRange WITH &&) WHERE (IsBlocking)`.
- `Payments`: unique `(Provider, ExternalId)`; `PaymentTransactions`: unique `(Provider, ProviderTxnId, Kind)`.
- `WebhookEvents`: unique `(Source, DedupKey)`.
- `Rooms`: unique `(HotelId, Number)`.
- `Bookings`: check `CheckOut > CheckIn`.
