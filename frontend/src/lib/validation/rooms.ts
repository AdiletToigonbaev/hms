import { z } from "zod";
import "./auth"; // локаль zod (русские сообщения)

// Правила совпадают с backend (Hms.Modules.Rooms.Application); окончательная проверка — в API.

const bedType = z.enum(["Single", "Double", "Twin", "Queen", "King", "Bunk"], { error: "Выберите тип кроватей." });

/** Пустое поле → undefined; иначе целое число. */
export const optionalInt = (min: number, max: number, message: string) =>
  z
    .string()
    .trim()
    .transform((v) => (v === "" ? undefined : Number(v)))
    .pipe(z.number({ error: message }).int(message).min(min, message).max(max, message).optional());

/** Сумма в сомах: запятая или точка, не более 2 знаков, больше нуля. */
export const money = (message: string) =>
  z
    .string()
    .trim()
    .transform((v) => v.replace(/\s/g, "").replace(",", "."))
    .refine((v) => /^\d{1,12}(\.\d{1,2})?$/.test(v) && Number(v) > 0, message)
    .transform(Number);

export const optionalMoney = (message: string) =>
  z
    .string()
    .trim()
    .transform((v) => (v === "" ? undefined : v))
    .pipe(money(message).optional());

/** Цена часа: поле может отсутствовать в форме; пусто — null (по часам не продаётся). */
const optionalHourly = z
  .string()
  .optional()
  .transform((v) => v ?? "")
  .pipe(optionalMoney("Цена часа — больше нуля, не более двух знаков после запятой."))
  .transform((v) => v ?? null);

const optionalText = (max: number) =>
  z
    .string()
    .trim()
    .max(max)
    .transform((v) => (v === "" ? null : v))
    .optional();

export const roomTypeSchema = z.object({
  name: z.string().trim().min(1, "Укажите название категории.").max(100),
  description: optionalText(2000),
  capacity: z.coerce.number({ error: "Вместимость — от 1 до 20 гостей." }).int().min(1, "Вместимость — от 1 до 20 гостей.").max(20, "Вместимость — от 1 до 20 гостей."),
  bedsCount: z.coerce.number({ error: "Кроватей — от 1 до 20." }).int().min(1, "Кроватей — от 1 до 20.").max(20, "Кроватей — от 1 до 20."),
  bedType,
  basePrice: money("Цена — больше нуля, не более двух знаков после запятой."),
  hourlyPrice: optionalHourly,
  sortOrder: z.coerce.number().int().min(0).max(1000).default(0),
});

export const roomSchema = z.object({
  roomTypeId: z.string().uuid({ error: "Выберите категорию." }),
  number: z
    .string()
    .trim()
    .min(1, "Укажите номер комнаты.")
    .max(10)
    .regex(/^[0-9A-Za-zА-Яа-я-]+$/, "Номер: цифры, буквы и дефис, до 10 символов."),
  floor: optionalInt(-5, 200, "Этаж — от -5 до 200."),
  capacity: optionalInt(1, 20, "Вместимость — от 1 до 20 гостей."),
  bedsCount: optionalInt(1, 20, "Кроватей — от 1 до 20."),
  bedType: z
    .string()
    .transform((v) => (v === "" ? undefined : v))
    .pipe(bedType.optional()),
  priceOverride: optionalMoney("Цена — больше нуля, не более двух знаков после запятой."),
  hourlyPriceOverride: optionalHourly,
  description: optionalText(2000),
});

export const statusChangeSchema = z.object({
  status: z.enum(["Available", "Cleaning", "Maintenance", "OutOfService"], { error: "Выберите новый статус." }),
  reason: z.string().trim().min(1, "Укажите причину смены статуса.").max(500),
});
