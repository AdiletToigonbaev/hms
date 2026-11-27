import { z } from "zod";
import "./auth"; // локаль zod (русские сообщения)

// Правила совпадают с backend (Hms.Modules.Messaging.Application); окончательная проверка — в API.

const optionalUuid = z
  .string()
  .trim()
  .transform((v) => (v === "" ? null : v))
  .pipe(z.string().uuid().nullable());

export const sendMessageSchema = z
  .object({
    guestId: optionalUuid,
    bookingId: optionalUuid,
    conversationId: optionalUuid,
    channel: z.enum(["Email", "WhatsApp"], { message: "Выберите канал: Email или WhatsApp." }),
    text: z.string().trim().min(1, "Введите текст сообщения.").max(2000, "Не больше 2000 символов."),
    subject: z
      .string()
      .trim()
      .max(150, "Тема — не больше 150 символов.")
      .transform((v) => (v === "" ? null : v))
      .nullable()
      .optional(),
  })
  .refine((v) => v.guestId !== null || v.conversationId !== null, { message: "Выберите гостя или диалог.", path: ["text"] });

export const templateSchema = z.object({
  emailSubject: z.string().trim().min(1, "Введите тему письма.").max(150, "Тема — не больше 150 символов."),
  emailBody: z.string().trim().min(1, "Введите текст письма.").max(4000, "Текст письма — не больше 4000 символов."),
  whatsAppBody: z.string().trim().min(1, "Введите текст WhatsApp.").max(1024, "Текст WhatsApp — не больше 1024 символов (ограничение Meta)."),
  whatsAppName: z
    .string()
    .trim()
    .transform((v) => (v === "" ? null : v))
    .pipe(z.string().regex(/^[a-z0-9_]+$/, "Имя шаблона в Meta — латиница в нижнем регистре, цифры и «_».").max(512).nullable()),
  whatsAppStatus: z.enum(["NotSubmitted", "Submitted", "Approved", "Rejected", "Paused", "Disabled"]),
  isActive: z.preprocess((v) => v === "on" || v === "true", z.boolean()),
});

const minutes = (min: number, max: number, message: string) => z.coerce.number({ message }).int(message).min(min, message).max(max, message);

export const messagingSettingsSchema = z.object({
  hourlyStartReminderMinutes: minutes(10, 1440, "Напоминание о почасовой брони — за 10 минут … 24 часа."),
  hourlyEndReminderMinutes: minutes(5, 180, "Напоминание о конце почасовой брони — за 5 … 180 минут."),
  nightlyStartReminderTime: z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/, "Время — в формате ЧЧ:ММ."),
  nightlyEndReminderMinutes: minutes(15, 720, "Напоминание о выезде — за 15 минут … 12 часов."),
  autoRepliesEnabled: z.preprocess((v) => v === "on" || v === "true", z.boolean()),
  greetingText: z.string().trim().min(1, "Введите приветствие.").max(1000),
  fallbackText: z.string().trim().min(1, "Введите ответ, если вопрос не понят.").max(1000),
  stopText: z.string().trim().min(1, "Введите ответ на отписку.").max(1000),
});

export const autoReplySchema = z.object({
  number: z.coerce.number().int().min(1, "Цифра пункта — от 1 до 9.").max(9, "Цифра пункта — от 1 до 9."),
  title: z.string().trim().min(1, "Введите название пункта.").max(40, "Название — не больше 40 символов."),
  keywords: z
    .string()
    .transform((v) =>
      v
        .split(",")
        .map((k) => k.trim())
        .filter(Boolean),
    )
    .pipe(z.array(z.string().max(30, "Ключевое слово — не больше 30 символов.")).max(20, "Не больше 20 ключевых слов.")),
  body: z.string().trim().min(1, "Введите ответ.").max(1500, "Ответ — не больше 1500 символов."),
  action: z.enum(["Text", "Operator"]),
  isActive: z.boolean(),
});

/** Пункты меню из формы: поля items.{i}.{name}. */
export function readAutoReplies(form: FormData) {
  const count = Number(form.get("count")) || 0;
  const items = [];
  for (let i = 0; i < count; i++) {
    if (form.get(`items.${i}.removed`) === "true") continue;
    items.push({
      number: form.get(`items.${i}.number`),
      title: form.get(`items.${i}.title`) ?? "",
      keywords: form.get(`items.${i}.keywords`) ?? "",
      body: form.get(`items.${i}.body`) ?? "",
      action: form.get(`items.${i}.action`) ?? "Text",
      isActive: form.get(`items.${i}.isActive`) === "on",
    });
  }
  return z.array(autoReplySchema).min(1, "В меню — хотя бы один пункт.").max(9, "В меню — не больше 9 пунктов.").safeParse(items);
}
