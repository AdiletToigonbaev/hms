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
  cancellationPolicy: CancellationPolicy;
  rules: string | null;
  version: number;
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
