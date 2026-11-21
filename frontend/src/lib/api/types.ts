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
  allowedActions: ("confirm" | "cancel" | "extend" | "reschedule" | "noShow")[];
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
