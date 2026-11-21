import { z } from "zod";
import "./auth"; // локаль zod (русские сообщения)

// Правила совпадают с backend (Hms.Modules.Guests.Application); окончательная проверка — в API.

const phone = (message: string) =>
  z
    .string()
    .trim()
    .transform((v) => (v === "" ? null : v))
    .pipe(z.string().regex(/^\+?[\d\s()-]{7,20}$/, message).nullable());

export const guestSchema = z
  .object({
    fullName: z.string().trim().min(1, "Укажите имя гостя.").max(200),
    phone: phone("Некорректный номер телефона. Пример: +996 XXX XXX XXX."),
    whatsAppPhone: phone("Некорректный номер WhatsApp. Пример: +996 XXX XXX XXX."),
    email: z
      .string()
      .trim()
      .transform((v) => (v === "" ? null : v))
      .pipe(z.string().email("Некорректный email.").max(256).nullable()),
    birthDate: z
      .string()
      .trim()
      .transform((v) => (v === "" ? null : v))
      .pipe(z.string().regex(/^\d{4}-\d{2}-\d{2}$/, "Некорректная дата рождения.").nullable()),
    notes: z
      .string()
      .trim()
      .max(2000)
      .transform((v) => (v === "" ? null : v)),
  })
  .refine((v) => Boolean(v.phone || v.email), { path: ["phone"], message: "Укажите телефон или email гостя." });

export const consentSchema = z.object({
  type: z.enum(["PersonalData", "WhatsApp"]),
  granted: z.enum(["true", "false"]).transform((v) => v === "true"),
  source: z.enum(["Portal", "FrontDesk", "Phone", "WhatsApp", "Written"], { error: "Укажите, откуда получено согласие." }),
});
