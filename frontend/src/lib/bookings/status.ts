import type { BookingStatus } from "@/lib/api/types";

/** Подписи статусов брони (ТЗ §5). Цвет всегда дублируется текстом. */
export const bookingStatusLabel: Record<BookingStatus, string> = {
  Pending: "Ожидает подтверждения",
  AwaitingPayment: "Ожидает оплаты",
  Confirmed: "Подтверждена",
  CheckedIn: "Гость заселён",
  CheckedOut: "Гость выехал",
  Cancelled: "Отменена",
  NoShow: "Неявка",
  Expired: "Истекла",
};

export const bookingStatusTone: Record<BookingStatus, string> = {
  Pending: "bg-status-cleaning/12 text-status-cleaning border-status-cleaning/30",
  AwaitingPayment: "bg-status-cleaning/12 text-status-cleaning border-status-cleaning/30",
  Confirmed: "bg-status-reserved/12 text-status-reserved border-status-reserved/30",
  CheckedIn: "bg-status-occupied/12 text-status-occupied border-status-occupied/30",
  CheckedOut: "bg-status-available/12 text-status-available border-status-available/30",
  Cancelled: "bg-status-out/12 text-status-out border-status-out/30",
  NoShow: "bg-status-maintenance/12 text-status-maintenance border-status-maintenance/30",
  Expired: "bg-status-out/12 text-status-out border-status-out/30",
};

export const bookingStatuses = Object.keys(bookingStatusLabel) as BookingStatus[];

/** Дни недели тарифа: битовая маска как в backend (Пн = 1 … Вс = 64). */
export const weekDays = [
  { bit: 1, short: "Пн" },
  { bit: 2, short: "Вт" },
  { bit: 4, short: "Ср" },
  { bit: 8, short: "Чт" },
  { bit: 16, short: "Пт" },
  { bit: 32, short: "Сб" },
  { bit: 64, short: "Вс" },
] as const;

export const ALL_DAYS = 127;

export function describeDays(mask: number): string {
  if (mask === ALL_DAYS) return "Все дни";
  if (mask === 48) return "Пт, Сб";
  return weekDays.filter((d) => (mask & d.bit) !== 0).map((d) => d.short).join(", ");
}

export function maskFromDays(values: readonly string[]): number {
  return values.map(Number).filter((v) => weekDays.some((d) => d.bit === v)).reduce((acc, v) => acc | v, 0);
}

/** Добавляет дни к дате YYYY-MM-DD (без часовых поясов). */
export function addDays(isoDate: string, days: number): string {
  const [y, m, d] = isoDate.split("-").map(Number);
  const date = new Date(Date.UTC(y, m - 1, d + days));
  return date.toISOString().slice(0, 10);
}

/** Сегодняшняя дата в часовом поясе гостиницы. */
export function hotelToday(timeZone: string, now: Date = new Date()): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone, year: "numeric", month: "2-digit", day: "2-digit" }).format(now);
}

/** Склонение «ночь». */
export function nightsLabel(n: number): string {
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod10 === 1 && mod100 !== 11) return `${n} ночь`;
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return `${n} ночи`;
  return `${n} ночей`;
}

/** Текущее время гостиницы «yyyy-MM-ddTHH:mm» — для сравнения с временем броней от API. */
export function hotelNowLocal(timeZone: string, now: Date = new Date()): string {
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).formatToParts(now);
  const get = (type: string) => parts.find((p) => p.type === type)?.value ?? "00";
  return `${get("year")}-${get("month")}-${get("day")}T${get("hour")}:${get("minute")}`;
}
