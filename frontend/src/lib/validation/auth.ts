import { z } from "zod";

// Сообщения zod по умолчанию — на русском (для полей без собственного текста ошибки).
z.config(z.locales.ru());

// Правила совпадают с backend (Hms.Modules.Identity.Application/Validators.cs); окончательная проверка — в API.
export const PASSWORD_MIN = 10;

const password = z.string().min(PASSWORD_MIN, `Пароль должен содержать не менее ${PASSWORD_MIN} символов.`).max(128);
const email = z.string().trim().email("Некорректный email.").max(256);
const optionalPhone = z
  .string()
  .trim()
  .transform((v) => (v === "" ? undefined : v))
  .optional();

export const loginSchema = z.object({
  login: z.string().trim().min(1, "Введите email или телефон."),
  password: z.string().min(1, "Введите пароль."),
});

export const registerSchema = z.object({
  fullName: z.string().trim().min(2, "Укажите ФИО.").max(200),
  email,
  phone: optionalPhone,
  password,
  acceptPersonalData: z.literal(true, { error: "Необходимо согласие на обработку персональных данных." }),
  acceptWhatsApp: z.boolean(),
  // Подтверждение регистрации: письмо или код в WhatsApp (если WhatsApp подключён) — решение 2026-11-24.
  confirmBy: z.enum(["Email", "WhatsApp"]).default("Email"),
})
  .refine((v) => v.confirmBy !== "WhatsApp" || v.phone !== undefined, { message: "Для подтверждения в WhatsApp укажите телефон.", path: ["phone"] })
  .refine((v) => v.confirmBy !== "WhatsApp" || v.acceptWhatsApp, { message: "Для подтверждения в WhatsApp нужно согласие на сообщения в WhatsApp.", path: ["acceptWhatsApp"] });

export const confirmPhoneSchema = z.object({
  userId: z.string().uuid(),
  code: z.string().trim().regex(/^d{6}$/, "Код — 6 цифр."),
});

export const emailSchema = z.object({ email });

export const resetPasswordSchema = z.object({
  userId: z.string().uuid(),
  token: z.string().min(1),
  password,
});

export const changePasswordSchema = z
  .object({ currentPassword: z.string().min(1, "Введите текущий пароль."), newPassword: password })
  .refine((v) => v.newPassword !== v.currentPassword, {
    path: ["newPassword"],
    message: "Новый пароль должен отличаться от текущего.",
  });

export const createStaffSchema = z
  .object({
    fullName: z.string().trim().min(2, "Укажите ФИО.").max(200),
    email: z.string().trim().transform((v) => (v === "" ? undefined : v)).pipe(email.optional()),
    phone: optionalPhone,
    role: z.enum(["Administrator", "Housekeeper"], { error: "Выберите роль." }),
    initialPassword: password,
  })
  .refine((v) => v.email || v.phone, { path: ["email"], message: "Укажите email или телефон — по нему сотрудник будет входить." });

/** Ошибки zod → { поле: первое сообщение }. */
export function fieldErrors(error: z.ZodError): Record<string, string> {
  const result: Record<string, string> = {};
  for (const issue of error.issues) {
    const key = String(issue.path[0] ?? "form");
    result[key] ??= issue.message;
  }
  return result;
}

/**
 * Отмечен ли чекбокс в отправленной форме. Семантика HTML: неотмеченный чекбокс не отправляется,
 * отмеченный — отправляется с любым значением (Base UI без `value` шлёт пустую строку).
 */
export function checkbox(form: FormData, name: string): boolean {
  const value = form.get(name);
  return value !== null && value !== "false";
}
