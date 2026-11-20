import "server-only";
import { cookies, headers } from "next/headers";
import { apiInternalUrl } from "@/lib/api/config";
import type { Problem } from "@/lib/api/types";
import { ACCESS_COOKIE } from "@/lib/auth/cookies";

export type ApiResult<T> = { ok: true; status: number; data: T } | { ok: false; status: number; problem: Problem };

interface ApiOptions {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  body?: unknown;
  /** Не подставлять access-токен (вход, регистрация, обновление токенов). */
  anonymous?: boolean;
  /** Явный токен (например, сразу после входа, когда cookie ещё не записана). */
  accessToken?: string;
}

/**
 * Вызов API с сервера Next.js (BFF). Браузер к API напрямую не обращается.
 * Пробрасывает IP клиента и correlation id, чтобы лимиты и аудит в API видели реального пользователя.
 */
export async function api<T>(path: string, options: ApiOptions = {}): Promise<ApiResult<T>> {
  const incoming = await headers();
  const requestHeaders: Record<string, string> = { Accept: "application/json" };
  const isForm = options.body instanceof FormData;
  if (options.body !== undefined && !isForm) requestHeaders["Content-Type"] = "application/json";

  const forwardedFor = incoming.get("x-forwarded-for") ?? incoming.get("x-real-ip");
  if (forwardedFor) requestHeaders["X-Forwarded-For"] = forwardedFor;
  const userAgent = incoming.get("user-agent");
  if (userAgent) requestHeaders["User-Agent"] = userAgent;
  const correlationId = incoming.get("x-correlation-id");
  if (correlationId) requestHeaders["X-Correlation-Id"] = correlationId;

  if (!options.anonymous) {
    const token = options.accessToken ?? (await cookies()).get(ACCESS_COOKIE)?.value;
    if (token) requestHeaders.Authorization = `Bearer ${token}`;
  }

  let response: Response;
  try {
    response = await fetch(`${apiInternalUrl()}/api/v1${path}`, {
      method: options.method ?? "GET",
      headers: requestHeaders,
      // FormData (загрузка файлов) отправляется как multipart — заголовок с boundary ставит fetch.
      body: options.body === undefined ? undefined : isForm ? (options.body as FormData) : JSON.stringify(options.body),
      cache: "no-store",
      signal: AbortSignal.timeout(isForm ? 60_000 : 10_000),
    });
  } catch {
    return { ok: false, status: 503, problem: { status: 503, code: "api.unavailable" } };
  }

  const text = await response.text();
  const json = text ? safeJson(text) : undefined;
  if (response.ok) return { ok: true, status: response.status, data: json as T };
  return { ok: false, status: response.status, problem: { status: response.status, ...(json as object | undefined) } };
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}
