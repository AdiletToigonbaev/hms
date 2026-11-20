import type { Role } from "@/lib/api/types";

/** Разделы приложения и роли, которым они доступны (UX; права проверяет API). */
export const areas = [
  { prefix: "/owner", roles: ["Owner"] },
  { prefix: "/admin", roles: ["Administrator", "Owner"] },
  { prefix: "/hk", roles: ["Housekeeper"] },
  { prefix: "/account", roles: ["Guest", "Owner", "Administrator", "Housekeeper"] },
  { prefix: "/change-password", roles: ["Guest", "Owner", "Administrator", "Housekeeper"] },
] as const satisfies ReadonlyArray<{ prefix: string; roles: readonly Role[] }>;

/** Страницы только для гостей сайта (неавторизованных): вошедшего пользователя уводим в его раздел. */
export const anonymousOnly = ["/login", "/register", "/forgot-password"] as const;

export function areaFor(pathname: string) {
  return areas.find((a) => pathname === a.prefix || pathname.startsWith(`${a.prefix}/`));
}

/** Стартовая страница пользователя по роли. */
export function homeFor(roles: readonly Role[]): string {
  if (roles.includes("Owner")) return "/owner";
  if (roles.includes("Administrator")) return "/admin";
  if (roles.includes("Housekeeper")) return "/hk";
  return "/account";
}

export function canAccess(pathname: string, roles: readonly Role[]): boolean {
  const area = areaFor(pathname);
  return !area || area.roles.some((r) => roles.includes(r));
}

/** Безопасный адрес возврата после входа: только относительный путь внутри сайта. */
export function safeNextPath(next: string | null | undefined): string | null {
  if (!next || !next.startsWith("/") || next.startsWith("//") || next.startsWith("/\\")) return null;
  return next;
}
