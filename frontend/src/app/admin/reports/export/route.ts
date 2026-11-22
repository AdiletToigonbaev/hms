import { cookies } from "next/headers";
import { apiInternalUrl } from "@/lib/api/config";
import { ACCESS_COOKIE } from "@/lib/auth/cookies";

const TYPES = new Set(["financial", "bookings", "guests", "rooms", "staff", "payments", "refunds", "expenses"]);
const ALLOWED = new Set(["period", "from", "to", "roomTypeId", "paymentMethod", "bookingStatus", "staffId", "fromHour", "toHour"]);

/**
 * Скачивание отчёта (Excel/PDF): браузер обращается к Next.js, токен берётся из httpOnly-cookie.
 * Права на отчёт и на выгрузку проверяет API; сюда пропускаются только известные параметры.
 */
export async function GET(request: Request) {
  const url = new URL(request.url);
  const type = url.searchParams.get("type") ?? "";
  const format = url.searchParams.get("format") ?? "";
  if (!TYPES.has(type) || (format !== "xlsx" && format !== "pdf")) return new Response("Неизвестный отчёт или формат.", { status: 400 });

  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return Response.redirect(new URL("/login", url), 303);

  const query = new URLSearchParams({ format });
  for (const [k, v] of url.searchParams) if (ALLOWED.has(k)) query.set(k, v);

  const upstream = await fetch(`${apiInternalUrl()}/api/v1/reports/${type}/export?${query}`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
    signal: AbortSignal.timeout(60_000),
  }).catch(() => null);
  if (!upstream) return new Response("Сервис отчётов недоступен.", { status: 502 });
  if (!upstream.ok) {
    const problem = (await upstream.json().catch(() => null)) as { title?: string } | null;
    return new Response(problem?.title ?? "Не удалось выгрузить отчёт.", { status: upstream.status, headers: { "content-type": "text/plain; charset=utf-8" } });
  }

  const headers = new Headers({ "x-content-type-options": "nosniff", "cache-control": "no-store" });
  for (const name of ["content-type", "content-length", "content-disposition"]) {
    const value = upstream.headers.get(name);
    if (value) headers.set(name, value);
  }
  return new Response(upstream.body, { status: 200, headers });
}
