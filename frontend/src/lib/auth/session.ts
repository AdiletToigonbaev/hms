import "server-only";
import { cache } from "react";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { api } from "@/lib/api/server";
import type { Me, Role } from "@/lib/api/types";
import { ACCESS_COOKIE } from "@/lib/auth/cookies";
import { homeFor } from "@/lib/auth/routes";

/**
 * Текущий пользователь по данным API (/me). Один запрос на рендер (React cache).
 * Вызывать только внутри <Suspense>: читает cookies запроса.
 */
export const getCurrentUser = cache(async (): Promise<Me | null> => {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return null;
  const result = await api<Me>("/me");
  return result.ok ? result.data : null;
});

/** Требует вход и одну из ролей; иначе — перенаправление. */
export async function requireUser(roles?: readonly Role[], currentPath?: string): Promise<Me> {
  const user = await getCurrentUser();
  if (!user) redirect(currentPath ? `/login?next=${encodeURIComponent(currentPath)}` : "/login");
  if (user.mustChangePassword) redirect("/change-password");
  if (roles && !roles.some((r) => user.roles.includes(r))) redirect(homeFor(user.roles));
  return user;
}
