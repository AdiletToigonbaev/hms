"use server";

import { revalidatePath, updateTag } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { api } from "@/lib/api/server";
import { HOTEL_NAME_TAG } from "@/lib/hotel";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import type { HotelSettings, Integration } from "@/lib/api/types";
import { settingsUpdateBody } from "@/lib/settings/rules";
import { checkbox, fieldErrors } from "@/lib/validation/auth";

const HOTEL_FIELDS = ["name", "address", "phone", "email", "whatsAppPhone", "twoGisUrl", "description", "rules"] as const;
/** Поля разделов настроек — для повторного показа введённых значений при ошибке. */
const SECTION_FIELDS = {
  rules: [
    "hourlyMinHours", "hourlyMaxHours", "hourlyFreeCancelMinutes", "hourlyLateCancelPenaltyHours", "hourlyNoShowAfterMinutes",
    "hourlyNoShowPenaltyHours", "hourlyPendingHoldMinutes", "checkInTime", "checkOutTime", "noShowCutoffTime",
    "freeCancelHoursBeforeCheckIn", "lateCancelPenaltyNights", "noShowPenaltyNights", "pendingHoldHours", "earlyDeparturePenaltyNights",
  ],
  rooms: ["cleaningBufferMinutes", "cleaningBufferNightlyMinutes"],
  payment: ["prepaymentPercent", "paymentHoldMinutes"],
  site: [],
} as const;

function authGuard(status: number) {
  if (status === 401) redirect("/login");
}

const text = (max: number) =>
  z
    .string()
    .trim()
    .max(max)
    .transform((v) => (v === "" ? null : v));
const time = z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/, "Время в формате ЧЧ:ММ.");
const int = (min: number, max: number, message: string) => z.coerce.number({ error: message }).int(message).min(min, message).max(max, message);

const hotelSchema = z.object({
  name: z.string().trim().min(1, "Укажите название гостиницы.").max(150),
  address: text(300),
  phone: text(32),
  email: z
    .string()
    .trim()
    .transform((v) => (v === "" ? null : v))
    .pipe(z.string().email("Некорректный email.").nullable()),
  // Номер и ссылку проверяет API (E.164; только https-адреса 2ГИС) — ошибки приходят по полям.
  whatsAppPhone: text(32),
  twoGisUrl: text(500),
  description: text(2000),
  rules: text(5000),
  version: z.coerce.number().int(),
});

export async function saveHotelAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = hotelSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, HOTEL_FIELDS);
  const { rules, ...hotel } = parsed.data;
  const result = await api("/hotel", { method: "PUT", body: hotel });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, HOTEL_FIELDS);
  }
  // Правила проживания хранятся в настройках бронирования — меняем только их.
  const current = await api<HotelSettings>("/hotel/settings");
  if (current.ok && (current.data.rules ?? null) !== rules) {
    const saved = await api("/hotel/settings", { method: "PUT", body: { ...settingsUpdateBody(current.data), rules } });
    if (!saved.ok) return withValues(toFormState(saved.problem), form, HOTEL_FIELDS);
  }
  revalidatePath("/owner/settings");
  // Название во вкладках и превью ссылок — сразу новое (кэш getCachedHotelName).
  updateTag(HOTEL_NAME_TAG);
  return { ok: true };
}

const rulesSchema = z
  .object({
    hourlyMinHours: int(1, 24, "Минимум часов — от 1 до 24."),
    hourlyMaxHours: int(1, 24, "Максимум часов — от 1 до 24."),
    hourlyFreeCancelMinutes: int(0, 10080, "От 0 минут до 7 дней."),
    hourlyLateCancelPenaltyHours: int(0, 24, "От 0 до 24 часов."),
    hourlyNoShowAfterMinutes: int(5, 720, "От 5 минут до 12 часов."),
    hourlyNoShowPenaltyHours: int(0, 24, "От 0 до 24 часов."),
    hourlyPendingHoldMinutes: int(5, 1440, "От 5 минут до 24 часов."),
    checkInTime: time,
    checkOutTime: time,
    noShowCutoffTime: time,
    freeCancelHoursBeforeCheckIn: int(0, 720, "От 0 до 720 часов."),
    lateCancelPenaltyNights: int(0, 30, "От 0 до 30 ночей."),
    noShowPenaltyNights: int(0, 30, "От 0 до 30 ночей."),
    pendingHoldHours: int(1, 168, "От 1 часа до 7 дней."),
    earlyDeparturePenaltyNights: int(0, 30, "От 0 до 30 ночей."),
  })
  .refine((v) => v.hourlyMaxHours >= v.hourlyMinHours, { path: ["hourlyMaxHours"], message: "Максимум часов не меньше минимума." });

const roomsSchema = z.object({
  cleaningBufferMinutes: int(0, 240, "Уборка — от 0 до 240 минут."),
  cleaningBufferNightlyMinutes: int(0, 240, "Уборка — от 0 до 240 минут."),
});
const paymentSchema = z.object({
  prepaymentPercent: int(0, 100, "Предоплата — от 0 до 100%."),
  paymentHoldMinutes: int(5, 1440, "От 5 минут до 24 часов."),
});

type SettingsBody = ReturnType<typeof settingsUpdateBody>;

/** Поля раздела поверх текущих настроек. Ошибка разбора — fieldErrors. */
function applySection(section: keyof typeof SECTION_FIELDS, form: FormData, body: SettingsBody): Record<string, string> | null {
  const data = Object.fromEntries(form);
  if (section === "rules") {
    const r = rulesSchema.safeParse(data);
    if (!r.success) return fieldErrors(r.error);
    const v = r.data;
    Object.assign(body, {
      hourlyMinHours: v.hourlyMinHours,
      hourlyMaxHours: v.hourlyMaxHours,
      checkInTime: v.checkInTime,
      checkOutTime: v.checkOutTime,
      noShowCutoffTime: v.noShowCutoffTime,
      pendingHoldHours: v.pendingHoldHours,
    });
    body.cancellationPolicy = {
      freeCancelHoursBeforeCheckIn: v.freeCancelHoursBeforeCheckIn,
      lateCancelPenaltyNights: v.lateCancelPenaltyNights,
      noShowPenaltyNights: v.noShowPenaltyNights,
      earlyDeparturePenaltyNights: v.earlyDeparturePenaltyNights,
    };
    body.hourlyPolicy = {
      freeCancelMinutesBeforeStart: v.hourlyFreeCancelMinutes,
      lateCancelPenaltyHours: v.hourlyLateCancelPenaltyHours,
      noShowAfterMinutes: v.hourlyNoShowAfterMinutes,
      noShowPenaltyHours: v.hourlyNoShowPenaltyHours,
      pendingHoldMinutes: v.hourlyPendingHoldMinutes,
      refundUnusedOnEarlyDeparture: checkbox(form, "hourlyRefundUnused"),
    };
    body.autoNoShowEnabled = checkbox(form, "autoNoShowEnabled");
  } else if (section === "rooms") {
    const r = roomsSchema.safeParse(data);
    if (!r.success) return fieldErrors(r.error);
    body.cleaningBufferMinutes = r.data.cleaningBufferMinutes;
    body.cleaningBufferNightlyMinutes = r.data.cleaningBufferNightlyMinutes;
    body.autoReleaseRoomAfterCleaning = checkbox(form, "autoReleaseRoomAfterCleaning");
  } else if (section === "payment") {
    const r = paymentSchema.safeParse(data);
    if (!r.success) return fieldErrors(r.error);
    body.prepaymentPercent = r.data.prepaymentPercent;
    body.paymentHoldMinutes = r.data.paymentHoldMinutes;
  } else {
    body.autoConfirmWebBookings = checkbox(form, "autoConfirmWebBookings");
    body.requireEmailConfirmation = checkbox(form, "requireEmailConfirmation");
  }
  return null;
}

/**
 * Сохранение одного раздела настроек: берём текущие настройки из API, меняем поля раздела, отправляем целиком.
 * Версия — из формы: если настройки успел изменить кто-то другой, API ответит 409, и данные не перезапишутся молча.
 */
export async function saveSettingsSectionAction(_: FormState, form: FormData): Promise<FormState> {
  const section = z.enum(["rules", "rooms", "payment", "site"]).parse(form.get("section"));
  const fields = SECTION_FIELDS[section];
  const current = await api<HotelSettings>("/hotel/settings");
  if (!current.ok) {
    authGuard(current.status);
    return toFormState(current.problem);
  }
  const body = settingsUpdateBody(current.data);
  body.version = Number(form.get("version"));
  const errors = applySection(section, form, body);
  if (errors) return withValues({ ok: false, fieldErrors: errors }, form, fields);

  const result = await api("/hotel/settings", { method: "PUT", body });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, fields);
  }
  revalidatePath("/owner/settings");
  return { ok: true };
}

const providers = ["MBank", "Optima", "WhatsApp", "Email"] as const;

export async function saveIntegrationAction(_: FormState, form: FormData): Promise<FormState> {
  const provider = z.enum(providers).parse(form.get("provider"));
  const mode = z.enum(["Sandbox", "Production"]).parse(form.get("mode") ?? "Sandbox");
  const publicConfig: Record<string, string> = {};
  for (const [key, value] of form.entries()) {
    if (key.startsWith("config.") && typeof value === "string") publicConfig[key.slice(7)] = value.trim();
  }
  const result = await api<Integration>(`/settings/integrations/${provider}`, {
    method: "PUT",
    body: { isEnabled: checkbox(form, "isEnabled"), mode, publicConfig },
  });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidatePath("/owner/integrations");
  return { ok: true };
}

export async function testIntegrationAction(_: FormState, form: FormData): Promise<FormState> {
  const provider = z.enum(providers).parse(form.get("provider"));
  const result = await api<Integration>(`/settings/integrations/${provider}/test`, { method: "POST" });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidatePath("/owner/integrations");
  return { ok: true };
}
