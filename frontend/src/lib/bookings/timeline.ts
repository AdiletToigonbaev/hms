// Геометрия календаря занятости: время гостиницы «yyyy-MM-ddTHH:mm» → минуты → пиксели и обратно.
// Арифметика — в UTC-минутах без часовых поясов: время уже приведено к гостинице сервером.

import { shiftDays, type IsoDate } from "@/lib/calendar";
import type { CalendarBooking } from "@/lib/api/types";
import type { RescheduleInput } from "@/lib/validation/bookings";

export const MINUTES_PER_DAY = 1440;

/** «yyyy-MM-ddTHH:mm» → минуты от эпохи (как если бы это было UTC). */
export function toMinutes(local: string): number {
  const [date, time = "00:00"] = local.split("T");
  const [y, m, d] = date.split("-").map(Number);
  const [h, mi] = time.split(":").map(Number);
  return Date.UTC(y, m - 1, d, h, mi) / 60_000;
}

/** Минуты от эпохи → «yyyy-MM-ddTHH:mm». */
export function fromMinutes(minutes: number): string {
  return new Date(minutes * 60_000).toISOString().slice(0, 16);
}

export interface Span {
  /** Отступ от начала шкалы, px. */
  left: number;
  width: number;
  /** Блок начинается до шкалы / заканчивается после — край обрезан. */
  clippedStart: boolean;
  clippedEnd: boolean;
}

/** Положение отрезка [start, end) на шкале, начинающейся с `from` 00:00 длиной `days` дней. */
export function span(start: string, end: string, from: IsoDate, days: number, pxPerMinute: number): Span | null {
  const origin = toMinutes(`${from}T00:00`);
  const total = days * MINUTES_PER_DAY;
  const s = toMinutes(start) - origin;
  const e = toMinutes(end) - origin;
  if (e <= 0 || s >= total) return null;
  const cs = Math.max(0, s);
  const ce = Math.min(total, e);
  return { left: cs * pxPerMinute, width: Math.max(2, (ce - cs) * pxPerMinute), clippedStart: s < 0, clippedEnd: e > total };
}

/** Округление до шага (в минутах), к ближайшему. */
export function snap(minutes: number, step: number): number {
  return Math.round(minutes / step) * step || 0;
}

export type CalendarView = "day" | "week";

/**
 * Шаг перетаскивания: почасовая бронь на шкале дня — час; на шкале недели и посуточная — сутки.
 * Посуточную бронь на шкале дня по времени не двигаем (только смена номера): её время задают часы заезда/выезда.
 */
export function moveStep(kind: CalendarBooking["kind"], view: CalendarView): number | null {
  if (kind === "Hourly") return view === "day" ? 60 : MINUTES_PER_DAY;
  return view === "week" ? MINUTES_PER_DAY : null;
}

/** Шаг изменения длительности (тянуть правый край): часы — на шкале дня, ночи — на шкале недели. */
export function resizeStep(kind: CalendarBooking["kind"], view: CalendarView): number | null {
  if (kind === "Hourly") return view === "day" ? 60 : null;
  return view === "week" ? MINUTES_PER_DAY : null;
}

export interface DragChange {
  roomId: string;
  /** Сдвиг начала, минуты (уже по шагу). */
  shift: number;
  /** Изменение длительности, минуты (уже по шагу). */
  stretch: number;
}

/**
 * Новое время брони после перетаскивания — в виде запроса переноса для API.
 * Почасовая: новое начало (целый час) и часы (в пределах min..max); посуточная: даты заезда и выезда (минимум одна ночь).
 * null — ничего не изменилось или длительность вышла за правила.
 */
export function rescheduleTarget(
  b: Pick<CalendarBooking, "kind" | "roomId" | "startsAt" | "endsAt" | "hours" | "nights">,
  change: DragChange,
  limits: { minHours: number; maxHours: number },
): RescheduleInput | null {
  if (change.shift === 0 && change.stretch === 0 && change.roomId === b.roomId) return null;
  if (b.kind === "Hourly") {
    const hours = (b.hours ?? 0) + Math.round(change.stretch / 60);
    if (hours < limits.minHours || hours > limits.maxHours) return null;
    const start = fromMinutes(toMinutes(b.startsAt) + change.shift);
    return { kind: "Hourly", roomId: change.roomId, date: start.slice(0, 10), startHour: Number(start.slice(11, 13)), hours };
  }
  const days = Math.round(change.shift / MINUTES_PER_DAY);
  const nights = b.nights + Math.round(change.stretch / MINUTES_PER_DAY);
  if (nights < 1) return null;
  const checkIn = shiftDays(b.startsAt.slice(0, 10), days);
  return { kind: "Nightly", roomId: change.roomId, checkIn, checkOut: shiftDays(checkIn, nights) };
}

/** Брони, начинающиеся или заканчивающиеся в этот день (заезды и выезды для стойки). */
export function dayMovements(bookings: CalendarBooking[], day: IsoDate) {
  const arrivals = bookings
    .filter((b) => b.startsAt.startsWith(day) && (b.status === "Pending" || b.status === "AwaitingPayment" || b.status === "Confirmed"))
    .sort((a, b) => a.startsAt.localeCompare(b.startsAt));
  const departures = bookings
    .filter((b) => b.endsAt.startsWith(day) && (b.status === "Confirmed" || b.status === "CheckedIn"))
    .sort((a, b) => a.endsAt.localeCompare(b.endsAt));
  const late = bookings.filter((b) => b.noShowDue);
  const awaiting = bookings
    .filter((b) => b.holdUntil !== null)
    .sort((a, b) => (a.holdUntil ?? "").localeCompare(b.holdUntil ?? ""));
  return { arrivals, departures, late, awaiting };
}

/** Новое время брони (с уборкой) из запроса переноса — для проверки пересечений в календаре. */
export function targetPeriod(
  target: RescheduleInput,
  b: Pick<CalendarBooking, "endsAt" | "blockedUntil">,
  times: { checkInTime: string; checkOutTime: string },
): { start: number; blockedUntil: number } {
  const buffer = toMinutes(b.blockedUntil) - toMinutes(b.endsAt);
  if (target.kind === "Hourly") {
    const start = toMinutes(`${target.date}T${String(target.startHour).padStart(2, "0")}:00`);
    return { start, blockedUntil: start + target.hours * 60 + buffer };
  }
  return {
    start: toMinutes(`${target.checkIn}T${times.checkInTime}`),
    blockedUntil: toMinutes(`${target.checkOut}T${times.checkOutTime}`) + buffer,
  };
}

/**
 * Пересекается ли новое время с другими бронями номера, видимыми в календаре (с уборкой).
 * Окончательно проверяет API (и ограничение БД) — это подсказка до отпускания блока.
 */
export function conflictsWith(
  bookings: CalendarBooking[],
  movingId: string,
  roomId: string,
  period: { start: number; blockedUntil: number },
): boolean {
  return bookings.some(
    (o) =>
      o.id !== movingId &&
      o.roomId === roomId &&
      o.status !== "CheckedOut" &&
      toMinutes(o.startsAt) < period.blockedUntil &&
      period.start < toMinutes(o.blockedUntil),
  );
}
