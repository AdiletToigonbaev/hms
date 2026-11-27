// Типы ответов API (backend: Hms.Modules.Identity.Application/Dtos.cs).
// TODO(Sprint 2): генерировать из OpenAPI (`npm run gen:api`), а не поддерживать вручную.

export type Role = "Owner" | "Administrator" | "Housekeeper" | "Guest";

export interface Me {
  id: string;
  fullName: string;
  email: string | null;
  emailConfirmed: boolean;
  phone: string | null;
  userType: "Staff" | "Guest";
  roles: Role[];
  permissions: string[];
  mustChangePassword: boolean;
}

export interface TokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: Me;
}

export interface Staff {
  id: string;
  fullName: string;
  email: string | null;
  phone: string | null;
  role: Role;
  roleDisplayName: string;
  isActive: boolean;
  mustChangePassword: boolean;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface PermissionInfo {
  code: string;
  group: string;
  description: string;
  ownerOnly: boolean;
}

export interface PermissionOverride {
  code: string;
  isGranted: boolean;
  limitAmount: number | null;
}

export interface StaffPermissions {
  userId: string;
  role: Role;
  rolePermissions: string[];
  overrides: PermissionOverride[];
  effective: string[];
  configurable: PermissionInfo[];
  /** Лимиты прав у роли (личный лимит сотрудника важнее). */
  roleLimits?: PermissionOverride[] | null;
}

/** Право роли и его лимит суммы. */
export interface RoleGrant {
  code: string;
  limitAmount: number | null;
}

/** Права роли для настройки владельцем. */
export interface RolePermissions {
  role: Role;
  displayName: string;
  grants: RoleGrant[];
  defaults: string[];
  configurable: PermissionInfo[];
  limited: string[];
  staffCount: number;
}

/** Ошибка API в формате RFC 9457 с кодом домена. */
export interface Problem {
  status: number;
  title?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

// ---- Гостиница (Hms.Modules.Hotel.Application) ----

export interface PublicHotelInfo {
  name: string;
  address: string | null;
  phone: string | null;
  email: string | null;
  description: string | null;
  checkInTime: string;
  checkOutTime: string;
  rules: string | null;
  freeCancelHoursBeforeCheckIn: number;
  hourlyMinHours: number;
  hourlyMaxHours: number;
  /** Почасовая бронь: бесплатная отмена не позже чем за столько минут до начала. */
  hourlyFreeCancelMinutes: number;
  /** Брони с сайта подтверждаются сразу (иначе — администратором). */
  autoConfirmWebBookings: boolean;
  /** Включены ли штрафы за позднюю отмену (по умолчанию — нет). */
  hasLatePenalties: boolean;
}

export interface Hotel {
  id: string;
  name: string;
  address: string | null;
  phone: string | null;
  email: string | null;
  description: string | null;
  timeZone: string;
  currency: string;
  version: number;
}

export interface CancellationPolicy {
  freeCancelHoursBeforeCheckIn: number;
  lateCancelPenaltyNights: number;
  noShowPenaltyNights: number;
  earlyDeparturePenaltyNights: number;
}

export interface HotelSettings {
  checkInTime: string;
  checkOutTime: string;
  onlinePaymentEnabled: boolean;
  prepaymentPercent: number;
  paymentHoldMinutes: number;
  pendingHoldHours: number;
  autoConfirmWebBookings: boolean;
  autoReleaseRoomAfterCleaning: boolean;
  requireEmailConfirmation: boolean;
  noShowCutoffTime: string;
  hourlyMinHours: number;
  hourlyMaxHours: number;
  cleaningBufferMinutes: number;
  cancellationPolicy: CancellationPolicy;
  rules: string | null;
  version: number;
  hourlyPolicy: HourlyPolicy;
  /** Отмечать неявку автоматически (фоновая задача). */
  autoNoShowEnabled: boolean;
  /** Уборка после посуточной брони, мин (после почасовой — cleaningBufferMinutes). */
  cleaningBufferNightlyMinutes: number;
}

/** Правила почасовых броней (настраивает владелец). */
export interface HourlyPolicy {
  freeCancelMinutesBeforeStart: number;
  lateCancelPenaltyHours: number;
  noShowAfterMinutes: number;
  noShowPenaltyHours: number;
  pendingHoldMinutes: number;
  refundUnusedOnEarlyDeparture: boolean;
}

export type IntegrationAvailability = "awaiting_access" | "secrets_missing" | "ready" | "active";

export interface Integration {
  provider: "MBank" | "Optima" | "WhatsApp" | "Email";
  displayName: string;
  isEnabled: boolean;
  mode: "Sandbox" | "Production";
  publicConfig: Record<string, string>;
  allowedPublicKeys: string[];
  adapterAvailable: boolean;
  secretsConfigured: boolean;
  availability: IntegrationAvailability;
  lastCheckAt: string | null;
  lastCheckStatus: string | null;
}

// ---- Номера (Hms.Modules.Rooms.Application) ----

export type RoomStatus = "Available" | "Occupied" | "Cleaning" | "Maintenance" | "OutOfService";
export type BedType = "Single" | "Double" | "Twin" | "Queen" | "King" | "Bunk";

export interface Amenity {
  id: string;
  code: string;
  name: string;
  icon: string | null;
  sortOrder: number;
}

export interface Photo {
  id: string;
  fileId: string;
  url: string;
  sortOrder: number;
}

export interface RoomType {
  id: string;
  name: string;
  description: string | null;
  capacity: number;
  bedsCount: number;
  bedType: BedType;
  basePrice: number;
  /** Цена за час; null — категория по часам не продаётся. */
  hourlyPrice: number | null;
  /** Своё время уборки категории (мин); null — как в настройках гостиницы. */
  cleaningHourlyMinutes: number | null;
  cleaningNightlyMinutes: number | null;
  sortOrder: number;
  isArchived: boolean;
  activeRoomCount: number;
  amenities: Amenity[];
  photos: Photo[];
  version: number;
}

export interface Room {
  id: string;
  number: string;
  floor: number | null;
  roomTypeId: string;
  roomTypeName: string;
  capacity: number | null;
  bedsCount: number | null;
  bedType: BedType | null;
  priceOverride: number | null;
  effectiveCapacity: number;
  effectiveBedsCount: number;
  effectiveBedType: BedType;
  effectivePrice: number;
  hourlyPriceOverride: number | null;
  effectiveHourlyPrice: number | null;
  /** Сейчас по брони — до этого времени гостиницы («yyyy-MM-ddTHH:mm»). */
  occupiedUntil: string | null;
  /** Ближайшая бронь сегодня. */
  nextBookingAt: string | null;
  description: string | null;
  status: RoomStatus;
  displayStatus: RoomStatus | "Reserved";
  manualStatusTargets: RoomStatus[];
  isArchived: boolean;
  photos: Photo[];
  version: number;
}

export interface RoomStatusHistoryItem {
  id: string;
  fromStatus: RoomStatus;
  toStatus: RoomStatus;
  source: "Manual" | "CheckIn" | "CheckOut" | "Housekeeping";
  reason: string | null;
  changedBy: string | null;
  changedByName: string | null;
  changedAt: string;
}

// ---------- Цены (модуль Pricing) ----------

export interface NightPrice {
  date: string;
  price: number;
  source: string;
}

export interface ServiceCharge {
  serviceId: string;
  name: string;
  quantity: number;
  unitPrice: number;
  perNight: boolean;
  total: number;
}

/** Расчёт стоимости — только с backend; на клиенте суммы не вычисляются. */
export interface PriceQuote {
  nights: NightPrice[];
  accommodationTotal: number;
  discountId: string | null;
  discountName: string | null;
  discountAmount: number;
  services: ServiceCharge[];
  servicesTotal: number;
  total: number;
  kind?: BookingKind;
  /** Почасовая бронь: часы с одной ценой, время гостиницы «yyyy-MM-ddTHH:mm». */
  hours?: HourPrice[] | null;
}

export type BookingKind = "Nightly" | "Hourly";

export interface HourPrice {
  from: string;
  to: string;
  hours: number;
  unitPrice: number;
  amount: number;
  source: string;
}

export interface Rate {
  id: string;
  roomTypeId: string;
  name: string;
  dateFrom: string;
  dateTo: string;
  daysOfWeek: number;
  price: number;
  priority: number;
  isActive: boolean;
  unit: "Night" | "Hour";
}

export type DiscountType = "Percent" | "Amount";

export interface Discount {
  id: string;
  name: string;
  code: string | null;
  type: DiscountType;
  value: number;
  validFrom: string | null;
  validTo: string | null;
  minNights: number;
  isActive: boolean;
  minHours: number;
}

export interface ExtraService {
  id: string;
  name: string;
  description: string | null;
  price: number;
  isPerNight: boolean;
  isPublic: boolean;
  isActive: boolean;
}

// ---------- Бронирования ----------

export type BookingStatus = "Pending" | "AwaitingPayment" | "Confirmed" | "CheckedIn" | "CheckedOut" | "Cancelled" | "NoShow" | "Expired";

export interface AvailableRoom {
  roomId: string;
  number: string;
  floor: number | null;
  roomTypeId: string;
  roomTypeName: string;
  capacity: number;
  quote: PriceQuote;
}

export interface BookingListItem {
  id: string;
  number: string;
  status: BookingStatus;
  checkIn: string;
  checkOut: string;
  nights: number;
  guestName: string;
  guestPhone: string | null;
  roomNumber: string;
  roomTypeName: string;
  totalAmount: number;
  createdAt: string;
  kind: BookingKind;
  startsAt: string;
  endsAt: string;
  hours: number | null;
  guestId: string | null;
}

export interface Booking {
  id: string;
  number: string;
  status: BookingStatus;
  source: "Admin" | "Web";
  checkIn: string;
  checkOut: string;
  nights: number;
  adults: number;
  children: number;
  guestName: string;
  guestPhone: string | null;
  guestEmail: string | null;
  comment: string | null;
  room: { roomId: string; number: string; roomTypeId: string; roomTypeName: string };
  accommodationAmount: number;
  discountAmount: number;
  servicesAmount: number;
  totalAmount: number;
  promoCode: string | null;
  price: PriceQuote | null;
  services: { serviceId: string; name: string; quantity: number; unitPrice: number; perNight: boolean; total: number }[];
  allowedActions: ("confirm" | "cancel" | "extend" | "reschedule" | "noShow" | "checkIn" | "checkOut" | "move")[];
  confirmedAt: string | null;
  cancelledAt: string | null;
  cancelReason: string | null;
  createdAt: string;
  version: number;
  kind: BookingKind;
  /** Время гостиницы «yyyy-MM-ddTHH:mm». */
  startsAt: string;
  endsAt: string;
  hours: number | null;
  extensionsAmount: number;
  extensions: BookingExtension[];
  guestId: string | null;
  /** Неподтверждённая бронь держит номер до этого времени (время гостиницы), потом истекает. */
  holdUntil: string | null;
  /** Начисленный штраф (к оплате; приём оплат — модуль оплат). */
  penaltyAmount: number;
  penaltyKind: "LateCancel" | "NoShow" | null;
  penaltyWaived: boolean;
  /** Фактическое заселение и выселение (время гостиницы). */
  actualCheckInAt: string | null;
  actualCheckOutAt: string | null;
  /** Перерасчёт при досрочном выезде — уже вычтен из стоимости. */
  earlyDepartureCredit: number;
  /** Изменение стоимости при переселении (пересчёт оставшегося времени); может быть отрицательным. */
  roomChangeAmount: number;
}

/** Куда можно переселить заселённого гостя; difference — изменение цены при пересчёте оставшегося времени. */
export interface MoveOptions {
  currentRoomNumber: string;
  remainingUnits: number;
  unit: "Hour" | "Night";
  total: number;
  rooms: { roomId: string; number: string; roomTypeName: string; difference: number }[];
}

/** Условия отмены сейчас (считает API по правилам гостиницы). */
export interface CancellationQuote {
  isFree: boolean;
  /** До какого момента отмена бесплатна (время гостиницы). */
  freeUntil: string | null;
  penalty: number;
  units: number;
  unit: "Hour" | "Night";
  canWaive: boolean;
  noShowPenalty: number;
}

export interface RescheduleQuote {
  startsAt: string;
  endsAt: string;
  roomId: string;
  roomNumber: string;
  oldTotal: number;
  newTotal: number;
  price: PriceQuote;
}

export interface CalendarRoom {
  id: string;
  number: string;
  floor: number | null;
  roomTypeId: string;
  roomTypeName: string;
  capacity: number;
  status: RoomStatus;
  sellsHourly: boolean;
  isSellable: boolean;
}

/** Бронь на шкале календаря; время — гостиницы, «yyyy-MM-ddTHH:mm». */
export interface CalendarBooking {
  id: string;
  number: string;
  roomId: string;
  status: BookingStatus;
  kind: BookingKind;
  startsAt: string;
  endsAt: string;
  /** Конец уборки после выезда. */
  blockedUntil: string;
  guestName: string;
  guestPhone: string | null;
  adults: number;
  children: number;
  hours: number | null;
  nights: number;
  totalAmount: number;
  holdUntil: string | null;
  noShowDue: boolean;
  canReschedule: boolean;
  version: number;
  /** Можно продлить (подтверждённая бронь или гость заселён). */
  canExtend: boolean;
}

export interface CalendarData {
  from: string;
  to: string;
  now: string;
  cleaningBufferMinutes: number;
  hourlyMinHours: number;
  hourlyMaxHours: number;
  checkInTime: string;
  checkOutTime: string;
  rooms: CalendarRoom[];
  bookings: CalendarBooking[];
}

export interface BookingExtension {
  kind: "Hour" | "Night";
  quantity: number;
  previousEndsAt: string;
  newEndsAt: string;
  amount: number;
  createdByName: string | null;
  createdAt: string;
}

export interface ExtensionQuote {
  kind: BookingKind;
  units: number;
  newEndsAt: string;
  amount: number;
  price: PriceQuote;
  maxUnits: number | null;
  nextBookingStartsAt: string | null;
}

export interface BookingHistoryItem {
  id: string;
  eventType: "created" | "status_changed" | "details_changed" | string;
  oldValues: Record<string, unknown> | null;
  newValues: Record<string, unknown> | null;
  changedBy: string | null;
  changedByName: string | null;
  changedAt: string;
  comment: string | null;
}

// ---------- Гости (CRM) ----------

export interface GuestStats {
  bookingsCount: number;
  /** Посещения — брони с заселением (CheckedIn/CheckedOut). */
  visitsCount: number;
  /** Сумма действующих и завершённых броней. */
  bookedAmount: number;
  lastStayAt: string | null;
  /** Фактически оплачено: поступления − аннулированные − выплаченные возвраты. */
  paidAmount: number;
}

export interface GuestListItem {
  id: string;
  fullName: string;
  phone: string | null;
  email: string | null;
  hasAccount: boolean;
  whatsAppConsent: boolean;
  isAnonymized: boolean;
  createdAt: string;
  stats: GuestStats;
}

export type ConsentType = "PersonalData" | "WhatsApp";
export type ConsentSource = "Portal" | "FrontDesk" | "Phone" | "WhatsApp" | "Written";

export interface ConsentState {
  type: ConsentType;
  granted: boolean;
  source: ConsentSource;
  recordedAt: string;
}

export interface Guest {
  id: string;
  fullName: string;
  phone: string | null;
  whatsAppPhone: string | null;
  email: string | null;
  birthDate: string | null;
  notes: string | null;
  source: "Admin" | "Booking" | "Portal";
  hasAccount: boolean;
  isAnonymized: boolean;
  createdAt: string;
  updatedAt: string;
  stats: GuestStats;
  consents: ConsentState[];
  version: number;
}

export interface GuestLookup {
  id: string;
  fullName: string;
  phone: string | null;
  email: string | null;
  bookingsCount: number;
}

export interface ConsentHistoryItem {
  id: string;
  type: ConsentType;
  granted: boolean;
  source: ConsentSource;
  recordedByName: string | null;
  recordedAt: string;
}

export interface GuestBooking {
  bookingId: string;
  number: string;
  status: BookingStatus;
  kind: BookingKind;
  startsAt: string;
  endsAt: string;
  roomNumber: string;
  totalAmount: number;
  createdAt: string;
}

// ---------- Финансы (Sprint 6) ----------

export type PaymentMethod = "Cash" | "BankTransfer" | "CardPos" | "MBank" | "Optima";
export type PaymentType = "Prepayment" | "Stay" | "Service" | "Penalty" | "Other" | "Reversal";
export type PaymentStatus = "Pending" | "Processing" | "Paid" | "Failed" | "Cancelled" | "RefundPending" | "PartiallyRefunded" | "Refunded" | "Reversed";
export type RefundStatus = "Requested" | "Approved" | "Rejected" | "Paid";

/** Время — гостиницы, «yyyy-MM-ddTHH:mm». */
export interface PaymentItem {
  id: string;
  type: PaymentType;
  method: PaymentMethod;
  channel: "Manual" | "Online";
  amount: number;
  status: PaymentStatus;
  refundedAmount: number;
  paidAt: string;
  bookingId: string | null;
  bookingNumber: string | null;
  guestName: string | null;
  registeredByName: string | null;
  comment: string | null;
  reversalOfPaymentId: string | null;
}

export interface PaymentTransaction {
  id: string;
  kind: "Charge" | "Refund" | "Reversal" | "StatusChange";
  amount: number;
  status: PaymentStatus;
  occurredAt: string;
  byName: string | null;
  comment: string | null;
}

export interface Refund {
  id: string;
  paymentId: string;
  bookingId: string | null;
  bookingNumber: string | null;
  amount: number;
  reason: string;
  method: PaymentMethod;
  status: RefundStatus;
  requestedAt: string;
  requestedByName: string | null;
  decidedAt: string | null;
  decidedByName: string | null;
  decisionComment: string | null;
  paidAt: string | null;
  paidByName: string | null;
  version: number;
}

export interface PaymentDetails {
  payment: PaymentItem;
  transactions: PaymentTransaction[];
  refunds: Refund[];
  allowedActions: ("reverse" | "refund")[];
  version: number;
}

export interface PaymentPage {
  page: Paged<PaymentItem>;
  totals: { total: number; byMethod: Partial<Record<PaymentMethod, number>>; count: number };
}

export interface BookingBalance {
  bookingId: string;
  number: string;
  bookingStatus: BookingStatus;
  due: number;
  received: number;
  refunded: number;
  /** Больше нуля — долг, меньше — переплата. */
  balance: number;
  prepaymentRequired: number;
  payments: PaymentItem[];
  refunds: Refund[];
}

export interface Debt {
  bookingId: string;
  number: string;
  bookingStatus: BookingStatus;
  kind: BookingKind;
  guestName: string;
  roomNumber: string | null;
  startsAt: string;
  endsAt: string;
  due: number;
  received: number;
  balance: number;
}

export interface DebtList {
  items: Debt[];
  total: number;
}

export interface RefundPage {
  page: Paged<Refund>;
  canApprove: boolean;
  approveLimit: number | null;
}

export interface Expense {
  id: string;
  categoryId: string;
  categoryName: string;
  amount: number;
  date: string;
  method: PaymentMethod;
  supplier: string | null;
  description: string;
  createdByName: string | null;
  createdAt: string;
  isVoided: boolean;
  voidReason: string | null;
  voidedByName: string | null;
  version: number;
}

export interface ExpensePage {
  page: Paged<Expense>;
  total: number;
  byCategory: Record<string, number>;
  canManage: boolean;
}

export interface ExpenseCategory {
  id: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
}

/** Можно ли заселить сейчас (считает API): окно заселения, готовность номера, оплата. */
export interface CheckInPreview {
  allowed: boolean;
  blockCode: string | null;
  blockMessage: string | null;
  roomNumber: string;
  roomStatus: RoomStatus;
  early: boolean;
  startsAt: string;
  endsAt: string;
  due: number;
  received: number;
  balance: number;
}

/** Что будет при выселении сейчас: досрочный выезд и перерасчёт, опоздание, долг, срок уборки. */
export interface CheckOutPreview {
  early: boolean;
  lateMinutes: number;
  unusedUnits: number;
  unit: "Hour" | "Night";
  credit: number;
  totalBefore: number;
  totalAfter: number;
  received: number;
  balanceAfter: number;
  plannedEndsAt: string;
  cleaningDueAt: string;
}

export type HousekeepingTaskStatus = "Pending" | "Assigned" | "InProgress" | "Completed" | "Inspected" | "Rejected";
export type TaskAction = "assign" | "delete" | "edit" | "inspect" | "take" | "start" | "complete";

export interface ChecklistItem {
  id: string;
  position: number;
  text: string;
  isDone: boolean;
}

/** Задача уборки — без персональных данных гостя. */
export interface HousekeepingTask {
  id: string;
  roomId: string;
  roomNumber: string;
  roomTypeName: string;
  roomStatus: RoomStatus;
  status: HousekeepingTaskStatus;
  priority: "Normal" | "High";
  origin: "CheckOut" | "Manual";
  assignedTo: string | null;
  assignedToName: string | null;
  dueAt: string | null;
  isOverdue: boolean;
  nextArrivalAt: string | null;
  notes: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  inspectedAt: string | null;
  inspectedByName: string | null;
  rejectReason: string | null;
  checklist: ChecklistItem[];
  allowedActions: TaskAction[];
  version: number;
}

export interface HousekeepingStaff {
  id: string;
  name: string;
  openTasks: number;
}

export interface ChecklistTemplate {
  id: string;
  name: string;
  items: string[];
  version: number;
}

export type MaintenanceType = "Defect" | "Damage";
export type MaintenanceSeverity = "Low" | "Normal" | "High" | "Critical";
export type MaintenanceStatus = "New" | "InProgress" | "Resolved" | "Rejected";

export interface MaintenanceRequest {
  id: string;
  roomId: string;
  roomNumber: string;
  taskId: string | null;
  type: MaintenanceType;
  severity: MaintenanceSeverity;
  description: string;
  status: MaintenanceStatus;
  reportedByName: string | null;
  createdAt: string;
  resolvedAt: string | null;
  resolvedByName: string | null;
  resolutionNote: string | null;
  roomStatus: RoomStatus;
  version: number;
}

// ---- Публичный сайт (Sprint 8) ----

/** Категория в каталоге сайта: только то, что видит гость (номера комнат и занятость не раскрываются). */
export interface PublicRoomType {
  id: string;
  name: string;
  description: string | null;
  capacity: number;
  bedsCount: number;
  bedType: string;
  /** Цена ночи «от». */
  priceFrom: number;
  /** Цена часа «от»; null — по часам категория не продаётся. */
  hourlyPriceFrom: number | null;
  amenities: Amenity[];
  photos: Photo[];
  /** Номера категории в продаже — гость выбирает конкретный номер. */
  rooms: PublicRoom[];
}

/** Номер на сайте: свои значения или категории; фото — только свои (пусто — показываем фото категории). */
export interface PublicRoom {
  id: string;
  number: string;
  floor: number | null;
  capacity: number;
  bedsCount: number;
  bedType: string;
  description: string | null;
  photos: Photo[];
}

/** Свободные категории на запрошенное время: количество и цена «от» за весь срок. */
export interface PublicAvailability {
  roomTypeId: string;
  roomTypeName: string;
  capacity: number;
  availableCount: number;
  nights: number;
  totalFrom: number;
  kind: "Hourly" | "Nightly";
  units: number;
  /** Свободные номера категории; цена — без промокода и услуг. */
  rooms: PublicAvailableRoom[];
}

export interface PublicAvailableRoom {
  roomId: string;
  number: string;
  floor: number | null;
  capacity: number;
  total: number;
}

// ---- Кабинет гостя (Sprint 8) ----

export interface MyBooking {
  id: string;
  number: string;
  status: BookingStatus;
  kind: "Hourly" | "Nightly";
  startsAt: string;
  endsAt: string;
  hours: number | null;
  nights: number;
  roomTypeId: string;
  roomTypeName: string;
  /** Номер комнаты — после заселения. */
  roomNumber: string | null;
  adults: number;
  children: number;
  price: PriceQuote | null;
  extensions: { kind: "Hour" | "Night"; quantity: number; newEndsAt: string; amount: number }[];
  totalAmount: number;
  penaltyAmount: number;
  penaltyKind: "LateCancel" | "NoShow" | null;
  earlyDepartureCredit: number;
  holdUntil: string | null;
  cancelReason: string | null;
  createdAt: string;
  allowedActions: ("cancel" | "extend")[];
  version: number;
}

export interface MyPayment {
  id: string;
  bookingId: string | null;
  bookingNumber: string | null;
  type: PaymentType;
  method: PaymentMethod;
  amount: number;
  refundedAmount: number;
  status: PaymentStatus;
  paidAt: string;
}

export interface MyRefund {
  id: string;
  bookingId: string | null;
  bookingNumber: string | null;
  amount: number;
  reason: string;
  status: RefundStatus;
  requestedAt: string;
  decidedAt: string | null;
  decisionComment: string | null;
  paidAt: string | null;
}

export interface MyBookingBalance {
  bookingId: string;
  due: number;
  received: number;
  refunded: number;
  /** Больше нуля — к оплате, меньше — переплата. */
  balance: number;
  prepaymentRequired: number;
  /** Сколько ещё можно запросить к возврату. */
  refundable: number;
  payments: MyPayment[];
  refunds: MyRefund[];
}

export interface PortalConsents {
  personalData: boolean;
  personalDataAt: string | null;
  whatsApp: boolean;
  whatsAppAt: string | null;
}

// ---- Отчёты и дашборды (Sprint 9) ----

export interface Kpi {
  accruedRevenue: number;
  cashIn: number;
  refundsPaid: number;
  netRevenue: number;
  expenses: number;
  netProfit: number;
  accruedProfit: number;
  occupancy: number;
  roomNightsSold: number;
  adr: number | null;
  revPar: number;
  hoursSold: number;
  avgHourPrice: number | null;
  revenuePerRoomHour: number;
  totalBookings: number;
  cancelledBookings: number;
  noShowBookings: number;
  cancellationRate: number;
  avgStayNights: number | null;
  avgStayHours: number | null;
  turnover: number;
}

export interface DayPoint {
  date: string;
  accruedRevenue: number;
  cashIn: number;
  expenses: number;
  occupancy: number;
  arrivals: number;
}

export interface OwnerDashboard {
  from: string;
  to: string;
  availableRooms: number;
  kpi: Kpi;
  outstanding: number;
  outstandingBookings: number;
  refundsRequested: number;
  refundsRequestedCount: number;
  daily: DayPoint[];
  cashByMethod: Record<string, number>;
  bookingsByStatus: Record<string, number>;
  revenueByRoomType: { name: string; amount: number }[];
  expensesByCategory: { name: string; amount: number }[];
  bookingsByKind: Record<string, number>;
  fromHour: number | null;
  toHour: number | null;
}

export interface TodayBooking {
  bookingId: string;
  number: string;
  kind: "Hourly" | "Nightly";
  status: BookingStatus;
  startsAt: string;
  endsAt: string;
  roomNumber: string;
  guestName: string | null;
}

export interface OperationsDashboard {
  date: string;
  arrivals: TodayBooking[];
  departures: TodayBooking[];
  inHouse: number;
  pendingConfirmations: number;
  roomsByStatus: Record<string, number>;
  openCleaningTasks: number;
  cleaningAwaitingInspection: number;
  occupancyToday: number;
  bookingsCreatedToday: number;
}

export type ReportColumnType = "text" | "money" | "int" | "number" | "percent" | "date" | "datetime";

export interface ReportTable {
  type: string;
  title: string;
  from: string;
  to: string;
  filters: string[];
  summary: { label: string; value: number | null; type: ReportColumnType; hint: string | null; example: string | null }[];
  columns: { key: string; title: string; type: ReportColumnType }[];
  rows: (string | number | null)[][];
  totals: (string | number | null)[] | null;
  note: string | null;
  fromHour: number | null;
  toHour: number | null;
}

// ---- Сообщения (Sprint 10) ----

export type MessageChannel = "Email" | "WhatsApp";
export type MessageStatus = "Queued" | "Sent" | "Delivered" | "Read" | "Failed" | "Skipped" | "Received";
export type MessageKind = "Notification" | "Manual" | "AutoReply" | "Incoming" | "VerificationCode";

export interface Message {
  id: string;
  guestId: string | null;
  guestName: string | null;
  bookingId: string | null;
  bookingNumber: string | null;
  channel: MessageChannel;
  direction: "Out" | "In";
  kind: MessageKind;
  templateCode: string | null;
  templateTitle: string | null;
  recipient: string | null;
  subject: string | null;
  body: string;
  status: MessageStatus;
  error: string | null;
  createdAt: string;
  sentAt: string | null;
  deliveredAt: string | null;
  readAt: string | null;
  sentByName: string | null;
  fallbackFor: string | null;
  canRetry: boolean;
}

export interface Conversation {
  id: string;
  phone: string;
  guestId: string | null;
  guestName: string | null;
  profileName: string | null;
  lastMessageAt: string;
  lastInboundAt: string | null;
  windowOpen: boolean;
  windowClosesAt: string | null;
  needsOperator: boolean;
  unreadCount: number;
  lastMessage: string | null;
  whatsAppConsent: boolean;
}

export interface SendOptions {
  emailAvailable: boolean;
  whatsAppAvailable: boolean;
  whatsAppConsent: boolean;
  windowOpen: boolean;
  windowClosesAt: string | null;
  manualTemplateApproved: boolean;
  whatsAppBlockReason: string | null;
}

export type WhatsAppTemplateStatus = "NotSubmitted" | "Submitted" | "Approved" | "Rejected" | "Paused" | "Disabled";

export interface MessageTemplate {
  code: string;
  language: string;
  title: string;
  event: string | null;
  emailAllowed: boolean;
  isAuthentication: boolean;
  variables: string[];
  emailSubject: string;
  emailBody: string;
  whatsAppName: string | null;
  whatsAppBody: string;
  whatsAppParameters: string[];
  whatsAppStatus: WhatsAppTemplateStatus;
  isActive: boolean;
  updatedAt: string;
  version: number;
}

export interface TemplatePreview {
  subject: string;
  emailBody: string;
  whatsAppBody: string;
}

export interface WhatsAppState {
  available: boolean;
  availability: "active" | "disabled" | "secrets_missing" | "not_configured";
  webhookConfigured: boolean;
  businessPhone: string | null;
}

export interface MessagingSettings {
  hourlyStartReminderMinutes: number;
  hourlyEndReminderMinutes: number;
  nightlyStartReminderTime: string;
  nightlyEndReminderMinutes: number;
  autoRepliesEnabled: boolean;
  greetingText: string;
  fallbackText: string;
  stopText: string;
  whatsApp: WhatsAppState;
  version: number;
}

export interface AutoReplyItem {
  number: number;
  title: string;
  keywords: string[];
  body: string;
  action: "Text" | "Operator";
  isActive: boolean;
}

export interface AutoReplies {
  items: AutoReplyItem[];
  variables: string[];
}

export interface ConfirmationChannels {
  email: boolean;
  whatsApp: boolean;
}
