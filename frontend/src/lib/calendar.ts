// Логика календаря выбора дат. Даты — строки YYYY-MM-DD (дата отеля, без часовых поясов);
// арифметика — в UTC, чтобы переход на летнее время и пояс устройства не сдвигали дни.

export type IsoDate = string;

const ISO = /^(\d{4})-(\d{2})-(\d{2})$/;

export function isIsoDate(value: unknown): value is IsoDate {
  if (typeof value !== "string") return false;
  const m = ISO.exec(value);
  if (!m) return false;
  const d = new Date(Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3])));
  return d.toISOString().slice(0, 10) === value;
}

function toUtc(iso: IsoDate): Date {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d));
}

function fromUtc(date: Date): IsoDate {
  return date.toISOString().slice(0, 10);
}

export function shiftDays(iso: IsoDate, days: number): IsoDate {
  const d = toUtc(iso);
  d.setUTCDate(d.getUTCDate() + days);
  return fromUtc(d);
}

export function daysBetween(from: IsoDate, to: IsoDate): number {
  return Math.round((toUtc(to).getTime() - toUtc(from).getTime()) / 86_400_000);
}

/** Первый день месяца `YYYY-MM-01`, сдвинутого на `months`. */
export function monthStart(iso: IsoDate, months = 0): IsoDate {
  const d = toUtc(iso);
  return fromUtc(new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + months, 1)));
}

/** День недели: 0 — понедельник … 6 — воскресенье. */
export function weekdayIndex(iso: IsoDate): number {
  return (toUtc(iso).getUTCDay() + 6) % 7;
}

/**
 * Сетка месяца: недели с понедельника, `null` — клетки соседних месяцев (не показываются).
 * Всегда 6 строк, чтобы два месяца рядом были одной высоты.
 */
export function monthGrid(month: IsoDate): (IsoDate | null)[][] {
  const first = monthStart(month);
  const nextMonth = monthStart(month, 1);
  const cells: (IsoDate | null)[] = Array.from({ length: weekdayIndex(first) }, () => null);
  for (let day = first; day < nextMonth; day = shiftDays(day, 1)) cells.push(day);
  while (cells.length < 42) cells.push(null);
  return Array.from({ length: 6 }, (_, i) => cells.slice(i * 7, i * 7 + 7));
}

const monthTitle = new Intl.DateTimeFormat("ru-RU", { month: "long", year: "numeric", timeZone: "UTC" });
const dayShort = new Intl.DateTimeFormat("ru-RU", { day: "numeric", month: "short", timeZone: "UTC" });
const weekdayShort = new Intl.DateTimeFormat("ru-RU", { weekday: "short", timeZone: "UTC" });
const dayFull = new Intl.DateTimeFormat("ru-RU", { weekday: "long", day: "numeric", month: "long", year: "numeric", timeZone: "UTC" });

/** «Декабрь 2026» (без «г.»). */
export function formatMonthTitle(month: IsoDate): string {
  const text = monthTitle.format(toUtc(month)).replace(/\s*г\.?$/, "");
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** «20 дек» — без точки сокращения. */
export function formatDayShort(iso: IsoDate): string {
  return dayShort.format(toUtc(iso)).replace(".", "");
}

/** «сб». */
export function formatWeekdayShort(iso: IsoDate): string {
  return weekdayShort.format(toUtc(iso)).replace(".", "");
}

/** «суббота, 20 декабря 2026 г.» — для экранных читалок. */
export function formatDayFull(iso: IsoDate): string {
  return dayFull.format(toUtc(iso));
}

export const weekdayHeaders = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"] as const;

export interface Range {
  start: IsoDate | null;
  end: IsoDate | null;
}

/**
 * Клик по дню. Первый клик (или клик после готового периода) — начало;
 * второй — конец, если он позже начала (для проживания) или не раньше (для периода фильтра).
 * Клик раньше начала переносит начало.
 */
export function pickDay(range: Range, day: IsoDate, allowSameDay: boolean): Range {
  if (!range.start || range.end) return { start: day, end: null };
  const valid = allowSameDay ? day >= range.start : day > range.start;
  return valid ? { start: range.start, end: day } : { start: day, end: null };
}

/** Положение дня относительно периода (с учётом предпросмотра при наведении). */
export function dayRole(day: IsoDate, range: Range, hover: IsoDate | null): "start" | "end" | "single" | "inside" | null {
  const { start } = range;
  if (!start) return null;
  const end = range.end ?? (hover && hover > start ? hover : null);
  if (day === start) return end && end !== start ? "start" : "single";
  if (!end) return null;
  if (day === end) return "end";
  return day > start && day < end ? "inside" : null;
}

/** Ближайшие выходные (ночи пятница → воскресенье) не раньше `from`. */
export function nextWeekend(from: IsoDate): Range {
  const offset = (4 - weekdayIndex(from) + 7) % 7; // 4 — пятница
  const friday = shiftDays(from, offset);
  return { start: friday, end: shiftDays(friday, 2) };
}
