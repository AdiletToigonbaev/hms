import { hotelTimeZone } from "@/i18n/config";

// Символ валюты задаём явно: в данных ICU у KGS нет локализованного обозначения,
// и разные браузеры/Node показывают «KGS» или «сом» по-разному.
const moneyFormatter = new Intl.NumberFormat("ru-RU", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});
const CURRENCY_SUFFIX = " сом"; // неразрывный пробел + «сом»

/**
 * Форматирует сумму в сомах. Суммы приходят с backend строкой или числом с 2 знаками
 * (numeric(14,2)); на клиенте деньги не вычисляются — только отображаются.
 */
export function formatMoney(amount: number | string): string {
  const value = typeof amount === "string" ? Number(amount) : amount;
  if (!Number.isFinite(value)) {
    throw new RangeError(`Некорректная сумма: ${amount}`);
  }
  return moneyFormatter.format(value) + CURRENCY_SUFFIX;
}

const dateFormatter = new Intl.DateTimeFormat("ru-RU", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  timeZone: "UTC",
});

/** Форматирует дату проживания `YYYY-MM-DD` (без времени и без сдвига часового пояса). */
export function formatStayDate(isoDate: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(isoDate);
  if (!match) {
    throw new RangeError(`Ожидается дата YYYY-MM-DD: ${isoDate}`);
  }
  const [, y, m, d] = match;
  return dateFormatter.format(new Date(Date.UTC(Number(y), Number(m) - 1, Number(d))));
}

const dateTimeFormatter = new Intl.DateTimeFormat("ru-RU", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: hotelTimeZone,
});

/**
 * Местное время гостиницы «yyyy-MM-ddTHH:mm» (так его отдаёт API для броней) → «20.11.2026 10:00».
 * Без пересчёта поясов: время уже гостиничное.
 */
export function formatHotelLocal(local: string): string {
  const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})/.exec(local);
  if (!match) {
    throw new RangeError(`Ожидается время YYYY-MM-DDTHH:mm: ${local}`);
  }
  return `${formatStayDate(match[1])} ${match[2]}`;
}

/** Только время «10:00» из местного времени гостиницы. */
export function formatHotelTime(local: string): string {
  return local.slice(11, 16);
}

/** Форматирует момент времени (ISO 8601 UTC с backend) во времени отеля. */
export function formatHotelDateTime(isoInstant: string): string {
  const date = new Date(isoInstant);
  if (Number.isNaN(date.getTime())) {
    throw new RangeError(`Некорректная дата: ${isoInstant}`);
  }
  return dateTimeFormatter.format(date);
}
