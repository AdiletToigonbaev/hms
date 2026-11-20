import type { Role } from "@/lib/api/types";

export interface AccessTokenClaims {
  sub: string;
  name?: string;
  roles: Role[];
  exp: number;
}

/**
 * Читает payload JWT БЕЗ проверки подписи — только для маршрутизации в интерфейсе
 * (куда перенаправить пользователя). Права всегда проверяет API.
 */
export function readAccessToken(token: string | undefined | null): AccessTokenClaims | null {
  if (!token) return null;
  const parts = token.split(".");
  if (parts.length !== 3) return null;
  try {
    const json = JSON.parse(base64UrlDecode(parts[1])) as Record<string, unknown>;
    const role = json.role;
    const roles = (Array.isArray(role) ? role : role ? [role] : []) as Role[];
    if (typeof json.sub !== "string" || typeof json.exp !== "number") return null;
    return { sub: json.sub, name: typeof json.name === "string" ? json.name : undefined, roles, exp: json.exp };
  } catch {
    return null;
  }
}

/** Истекает ли токен в ближайшие `skewSeconds` секунд. */
export function isExpiring(claims: AccessTokenClaims, nowMs = Date.now(), skewSeconds = 30): boolean {
  return claims.exp * 1000 - nowMs <= skewSeconds * 1000;
}

function base64UrlDecode(input: string): string {
  const base64 = input.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(input.length / 4) * 4, "=");
  const binary = atob(base64);
  const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}
