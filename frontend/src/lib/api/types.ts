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
