import { z } from "zod";
import { money, optionalInt } from "./rooms";
import "./auth"; // локаль zod (русские сообщения)

// Правила совпадают с backend (Hms.Modules.Bookings.Application, Hms.Modules.Pricing.Application); окончательная проверка — в API.

export const MAX_NIGHTS = 60;
const isoDate = (message: string) => z.string().regex(/^\d{4}-\d{2}-\d{2}$/, message);
const nightsBetween = (from: string, to: string) => (Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000;

const optionalText = (max: number) =>
  z
    .string()
    .trim()
    .max(max)
    .transform((v) => (v === "" ? null : v))
    .optional();

const optionalDate = z
  .string()
  .trim()
  .transform((v) => (v === "" ? null : v))
  .pipe(isoDate("Дата в формате ГГГГ-ММ-ДД.").nullable());

/** Даты проживания: выезд позже заезда, не более MAX_NIGHTS ночей. */
export const staySchema = z
  .object({
    checkIn: isoDate("Укажите дату заезда."),
    checkOut: isoDate("Укажите дату выезда."),
    adults: z.coerce.number().int().min(1, "Взрослых — от 1 до 20.").max(20, "Взрослых — от 1 до 20."),
    children: z.coerce.number().int().min(0, "Детей — от 0 до 20.").max(20, "Детей — от 0 до 20.").default(0),
  })
  .refine((v) => v.checkOut > v.checkIn, { path: ["checkOut"], message: "Дата выезда должна быть позже даты заезда." })
  .refine((v) => nightsBetween(v.checkIn, v.checkOut) <= MAX_NIGHTS, { path: ["checkOut"], message: `Не более ${MAX_NIGHTS} ночей в одной брони.` });

const guestFields = {
  guestName: z.string().trim().min(1, "Укажите имя гостя.").max(200),
  guestPhone: z
    .string()
    .trim()
    .transform((v) => (v === "" ? null : v))
    .pipe(z.string().regex(/^\+?[\d\s()-]{7,20}$/, "Некорректный номер телефона. Пример: +996 555 123 456.").nullable()),
  guestEmail: z
    .string()
    .trim()
    .transform((v) => (v === "" ? null : v))
    .pipe(z.string().email("Некорректный email.").max(256).nullable()),
  comment: optionalText(1000),
};

const contactRequired = (v: { guestPhone: string | null; guestEmail: string | null }) => Boolean(v.guestPhone || v.guestEmail);

export const createBookingSchema = staySchema
  .and(
    z.object({
      roomId: z.string().uuid({ error: "Выберите номер." }),
      ...guestFields,
      promoCode: z
        .string()
        .trim()
        .max(30)
        .transform((v) => (v === "" ? null : v.toUpperCase())),
    }),
  )
  .refine(contactRequired, { path: ["guestPhone"], message: "Укажите телефон или email гостя для связи." });

export const updateBookingSchema = z
  .object({
    ...guestFields,
    adults: z.coerce.number().int().min(1, "Взрослых — от 1 до 20.").max(20, "Взрослых — от 1 до 20."),
    children: z.coerce.number().int().min(0).max(20),
  })
  .refine(contactRequired, { path: ["guestPhone"], message: "Укажите телефон или email гостя для связи." });

export const cancelSchema = z.object({ reason: z.string().trim().min(1, "Укажите причину отмены.").max(500) });

// ---------- Цены ----------

export const rateSchema = z
  .object({
    roomTypeId: z.string().uuid({ error: "Выберите категорию." }),
    name: z.string().trim().min(1, "Укажите название тарифа.").max(100),
    dateFrom: isoDate("Укажите начало периода."),
    dateTo: isoDate("Укажите конец периода."),
    price: money("Цена — больше нуля, не более двух знаков после запятой."),
    priority: z.coerce.number().int().min(0, "Приоритет — от 0 до 100.").max(100, "Приоритет — от 0 до 100.").default(0),
  })
  .refine((v) => v.dateTo >= v.dateFrom, { path: ["dateTo"], message: "Конец периода не раньше начала." })
  .refine((v) => nightsBetween(v.dateFrom, v.dateTo) <= 730, { path: ["dateTo"], message: "Период тарифа — не более двух лет." });

export const discountSchema = z
  .object({
    name: z.string().trim().min(1, "Укажите название скидки.").max(100),
    code: z
      .string()
      .trim()
      .transform((v) => (v === "" ? null : v.toUpperCase()))
      .pipe(z.string().regex(/^[A-Z0-9_-]{3,30}$/, "Промокод: 3–30 символов — латиница, цифры, «-» и «_».").nullable()),
    type: z.enum(["Percent", "Amount"], { error: "Выберите тип скидки." }),
    value: money("Значение — больше нуля."),
    validFrom: optionalDate,
    validTo: optionalDate,
    minNights: optionalInt(0, 365, "Минимум ночей — от 0 до 365.").transform((v) => v ?? 0),
  })
  .refine((v) => v.type !== "Percent" || v.value <= 100, { path: ["value"], message: "Процент — не более 100." })
  .refine((v) => !v.validFrom || !v.validTo || v.validTo >= v.validFrom, { path: ["validTo"], message: "Конец периода не раньше начала." });

export const serviceSchema = z.object({
  name: z.string().trim().min(1, "Укажите название услуги.").max(100),
  description: optionalText(500),
  price: money("Цена — больше нуля, не более двух знаков после запятой."),
});
