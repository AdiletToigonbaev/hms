import { z } from "zod";
import { money } from "./rooms";
import "./auth"; // локаль zod (русские сообщения)

const methods = ["Cash", "BankTransfer", "CardPos", "MBank", "Optima"] as const;
const optionalText = (max: number) =>
  z
    .string()
    .trim()
    .max(max, `Не больше ${max} символов.`)
    .optional()
    .transform((v) => (v ? v : null));

/** Ручная регистрация платежа: сумма с запятой или точкой, до двух знаков. */
export const paymentSchema = z.object({
  bookingId: z
    .string()
    .uuid()
    .optional()
    .or(z.literal("").transform(() => undefined)),
  type: z.enum(["Prepayment", "Stay", "Service", "Penalty", "Other"], { error: "Выберите, за что платёж." }),
  method: z.enum(methods, { error: "Выберите способ оплаты." }),
  amount: money("Сумма — число больше нуля, до двух знаков после запятой."),
  comment: optionalText(500),
});

export const reverseSchema = z.object({ reason: z.string().trim().min(1, "Укажите причину аннулирования.").max(500) });

export const refundRequestSchema = z.object({
  paymentId: z.string().uuid(),
  amount: money("Сумма — число больше нуля, до двух знаков после запятой."),
  reason: z.string().trim().min(1, "Укажите причину возврата.").max(500),
  method: z.enum(methods, { error: "Выберите, как вернуть деньги." }),
});

export const refundDecisionSchema = z.object({ comment: optionalText(500) });

export const rejectSchema = z.object({ comment: z.string().trim().min(1, "Укажите причину отказа.").max(500) });

export const expenseSchema = z.object({
  categoryId: z.string().uuid({ error: "Выберите категорию." }),
  amount: money("Сумма — число больше нуля, до двух знаков после запятой."),
  date: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, "Выберите дату."),
  method: z.enum(methods, { error: "Выберите способ оплаты." }),
  supplier: optionalText(200),
  description: z.string().trim().min(1, "Опишите расход.").max(500),
});

export const voidSchema = z.object({ reason: z.string().trim().min(1, "Укажите причину аннулирования.").max(500) });

export const categorySchema = z.object({
  name: z.string().trim().min(1, "Укажите название.").max(100),
  sortOrder: z.coerce.number().int().min(0).max(1000).default(100),
});
