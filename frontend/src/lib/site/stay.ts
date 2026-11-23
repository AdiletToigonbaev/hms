import { addDays } from "@/lib/bookings/status";
import { isIsoDate } from "@/lib/calendar";
import { hourlySchema, staySchema } from "@/lib/validation/bookings";

/**
 * Параметры поиска на сайте (адресная строка): вид брони, время, гости, категория (фильтр) и выбранный номер.
 * По умолчанию — почасовая бронь с ближайшего часа (основной поток гостиницы).
 */
export interface StaySearch {
  hourly: boolean;
  date: string;
  startHour: number;
  hours: number;
  checkIn: string;
  checkOut: string;
  adults: number;
  children: number;
  roomTypeId: string | null;
  /** Номер, выбранный гостем (решение 2026-11-23): шаг подтверждения брони. */
  roomId: string | null;
}

type Params = Record<string, string | string[] | undefined>;

const str = (v: string | string[] | undefined) => (typeof v === "string" ? v : undefined);
const uuid = /^[0-9a-f-]{36}$/i;
const pad = (n: number) => String(n).padStart(2, "0");

/**
 * Читает параметры поиска с подстановкой разумных значений: сегодня с ближайшего часа (после 23:00 — завтра с 10:00),
 * минимум часов из настроек, на одну ночь, двое взрослых. Неверные значения не ломают страницу — заменяются.
 */
export function readStay(params: Params, today: string, nowHour: number, minHours: number, maxHours: number): StaySearch {
  const late = nowHour >= 23;
  const date = isIsoDate(str(params.date)) && str(params.date)! >= today ? str(params.date)! : late ? addDays(today, 1) : today;
  const earliest = date === today ? nowHour + 1 : 0;
  const rawHour = Number(str(params.startHour));
  const startHour = Number.isInteger(rawHour) && rawHour >= earliest && rawHour <= 23 ? rawHour : late ? 10 : Math.min(earliest, 23);
  const rawHours = Number(str(params.hours));
  const hours = Number.isInteger(rawHours) && rawHours >= minHours && rawHours <= maxHours ? rawHours : minHours;
  const checkIn = isIsoDate(str(params.checkIn)) && str(params.checkIn)! >= today ? str(params.checkIn)! : today;
  const checkOut = isIsoDate(str(params.checkOut)) && str(params.checkOut)! > checkIn ? str(params.checkOut)! : addDays(checkIn, 1);
  const adults = clamp(Number(str(params.adults)), 1, 10, 2);
  const children = clamp(Number(str(params.children)), 0, 10, 0);
  const type = str(params.roomTypeId);
  const room = str(params.roomId);
  return {
    hourly: str(params.kind) !== "nightly",
    date,
    startHour,
    hours,
    checkIn,
    checkOut,
    adults,
    children,
    roomTypeId: type && uuid.test(type) ? type : null,
    roomId: room && uuid.test(room) ? room : null,
  };
}

function clamp(value: number, min: number, max: number, fallback: number) {
  return Number.isInteger(value) && value >= min && value <= max ? value : fallback;
}

/** Параметры адресной строки сайта (для ссылок «изменить», «выбрать номер», возврата после входа). */
export function stayQuery(s: StaySearch, extra: Record<string, string> = {}): URLSearchParams {
  const q = new URLSearchParams(
    s.hourly
      ? { kind: "hourly", date: s.date, startHour: String(s.startHour), hours: String(s.hours) }
      : { kind: "nightly", checkIn: s.checkIn, checkOut: s.checkOut },
  );
  q.set("adults", String(s.adults));
  q.set("children", String(s.children));
  if (s.roomTypeId) q.set("roomTypeId", s.roomTypeId);
  if (s.roomId) q.set("roomId", s.roomId);
  for (const [k, v] of Object.entries(extra)) q.set(k, v);
  return q;
}

/** Запрос к API (/public/availability, /public/bookings/quote, /me/bookings). */
export function stayApiBody(s: Omit<StaySearch, "roomTypeId" | "roomId">) {
  return s.hourly
    ? { kind: "Hourly", date: s.date, startHour: s.startHour, hours: s.hours, adults: s.adults, children: s.children }
    : { kind: "Nightly", checkIn: s.checkIn, checkOut: s.checkOut, adults: s.adults, children: s.children };
}

export function stayApiQuery(s: StaySearch): URLSearchParams {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(stayApiBody(s))) q.set(k, String(v));
  return q;
}

/** Проверка тем же правилом, что и в кабинете администратора (точные границы — в API). */
export function validateStay(s: StaySearch): string | null {
  const result = s.hourly ? hourlySchema.safeParse(s) : staySchema.safeParse(s);
  return result.success ? null : (result.error.issues[0]?.message ?? "Проверьте даты.");
}

/** «22.11, 14:00–16:00 · 2 ч» или «22.11 — 24.11 · 2 ночи». */
export function staySummary(s: StaySearch, formatDate: (iso: string) => string, nights: (n: number) => string): string {
  if (s.hourly) return `${formatDate(s.date)}, ${pad(s.startHour)}:00–${pad((s.startHour + s.hours) % 24)}:00 · ${s.hours} ч`;
  const n = Math.round((Date.parse(s.checkOut) - Date.parse(s.checkIn)) / 86_400_000);
  return `${formatDate(s.checkIn)} — ${formatDate(s.checkOut)} · ${nights(n)}`;
}

/** Текущий час по времени гостиницы. */
export function hotelHour(timeZone: string, now: Date = new Date()): number {
  return Number(new Intl.DateTimeFormat("en-GB", { timeZone, hour: "2-digit", hourCycle: "h23" }).format(now));
}
