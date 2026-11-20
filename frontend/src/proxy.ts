import { NextResponse, type NextRequest } from "next/server";
import { apiInternalUrl } from "@/lib/api/config";
import type { TokenResponse } from "@/lib/api/types";
import { ACCESS_COOKIE, MUST_CHANGE_COOKIE, REFRESH_COOKIE, allSessionCookies, sessionCookies } from "@/lib/auth/cookies";
import { isExpiring, readAccessToken } from "@/lib/auth/jwt";
import { anonymousOnly, areaFor, canAccess, homeFor } from "@/lib/auth/routes";

type RefreshOutcome = { kind: "ok"; tokens: TokenResponse } | { kind: "invalid" } | { kind: "retry" };

/**
 * До рендера страницы (и вызова Server Action):
 * 1) если access-токен истёк, а refresh есть — обновляет пару и передаёт новый токен в рендер;
 * 2) перенаправляет по ролям (удобство интерфейса; права проверяет API).
 */
export async function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  const refreshToken = request.cookies.get(REFRESH_COOKIE)?.value;
  let claims = readAccessToken(request.cookies.get(ACCESS_COOKIE)?.value);
  let refreshed: TokenResponse | null = null;
  let clear = false;

  if ((!claims || isExpiring(claims)) && refreshToken) {
    const outcome = await refresh(refreshToken, request);
    if (outcome.kind === "ok") {
      refreshed = outcome.tokens;
      claims = readAccessToken(refreshed.accessToken);
    } else if (outcome.kind === "invalid") {
      clear = true;
      claims = null;
    }
    // "retry": параллельный запрос той же вкладки уже обновил токены — его ответ запишет cookie.
  }

  const mustChange = refreshed ? refreshed.user.mustChangePassword : request.cookies.get(MUST_CHANGE_COOKIE)?.value === "1";
  const area = areaFor(pathname);
  let response: NextResponse;

  if (area && !claims) {
    const login = new URL("/login", request.url);
    login.searchParams.set("next", pathname + search);
    response = NextResponse.redirect(login);
  } else if (claims && mustChange && area && pathname !== "/change-password") {
    response = NextResponse.redirect(new URL("/change-password", request.url));
  } else if (claims && area && !canAccess(pathname, claims.roles)) {
    response = NextResponse.redirect(new URL(homeFor(claims.roles), request.url));
  } else if (claims && (anonymousOnly as readonly string[]).includes(pathname)) {
    response = NextResponse.redirect(new URL(homeFor(claims.roles), request.url));
  } else {
    if (refreshed) {
      // Новый токен виден серверным компонентам уже в этом запросе.
      request.cookies.set(ACCESS_COOKIE, refreshed.accessToken);
      request.cookies.set(REFRESH_COOKIE, refreshed.refreshToken);
    } else if (clear) {
      for (const name of allSessionCookies) request.cookies.delete(name);
    }
    response = NextResponse.next({ request: { headers: request.headers } });
  }

  if (refreshed) {
    for (const cookie of sessionCookies(refreshed)) response.cookies.set(cookie);
  } else if (clear) {
    for (const name of allSessionCookies) response.cookies.delete(name);
  }
  return response;
}

async function refresh(refreshToken: string, request: NextRequest): Promise<RefreshOutcome> {
  try {
    const headers: Record<string, string> = { "Content-Type": "application/json" };
    const forwardedFor = request.headers.get("x-forwarded-for");
    if (forwardedFor) headers["X-Forwarded-For"] = forwardedFor;
    const userAgent = request.headers.get("user-agent");
    if (userAgent) headers["User-Agent"] = userAgent;

    const response = await fetch(`${apiInternalUrl()}/api/v1/auth/refresh`, {
      method: "POST",
      headers,
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
      signal: AbortSignal.timeout(5_000),
    });
    if (response.ok) return { kind: "ok", tokens: (await response.json()) as TokenResponse };
    if (response.status === 401) {
      const problem = (await response.json().catch(() => ({}))) as { code?: string };
      return problem.code === "auth.refresh_token_rotated" ? { kind: "retry" } : { kind: "invalid" };
    }
    return { kind: "retry" };
  } catch {
    // API недоступно — не выкидываем пользователя из сессии.
    return { kind: "retry" };
  }
}

export const config = {
  // Все страницы, кроме служебных файлов и собственных API-маршрутов Next.js.
  // Точка — классом [.]: экранирование "\." теряется при сборке matcher, и шаблон начинает совпадать с любым путём.
  matcher: ["/((?!api|_next/static|_next/image|favicon[.]ico|.*[.][a-zA-Z0-9]+$).*)"],
};
