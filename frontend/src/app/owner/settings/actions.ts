"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { api } from "@/lib/api/server";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import type { Integration } from "@/lib/api/types";
import { checkbox, fieldErrors } from "@/lib/validation/auth";

const HOTEL_FIELDS = ["name", "address", "phone", "email", "description"] as const;
const SETTINGS_FIELDS = [
  "checkInTime",
  "checkOutTime",
  "noShowCutoffTime",
  "prepaymentPercent",
  "paymentHoldMinutes",
  "pendingHoldHours",
  "freeCancelHoursBeforeCheckIn",
  "lateCancelPenaltyNights",
  "noShowPenaltyNights",
  "earlyDeparturePenaltyNights",
  "rules",
] as const;

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
  description: text(2000),
  version: z.coerce.number().int(),
});

export async function saveHotelAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = hotelSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, HOTEL_FIELDS);
  const result = await api("/hotel", { method: "PUT", body: parsed.data });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, HOTEL_FIELDS);
  }
  revalidatePath("/owner/settings");
  return { ok: true };
}

const settingsSchema = z.object({
  checkInTime: time,
  checkOutTime: time,
  noShowCutoffTime: time,
  prepaymentPercent: int(0, 100, "Предоплата — от 0 до 100%."),
  paymentHoldMinutes: int(5, 1440, "От 5 минут до 24 часов."),
  pendingHoldHours: int(1, 168, "От 1 часа до 7 дней."),
  freeCancelHoursBeforeCheckIn: int(0, 720, "От 0 до 720 часов."),
  lateCancelPenaltyNights: int(0, 30, "От 0 до 30 ночей."),
  noShowPenaltyNights: int(0, 30, "От 0 до 30 ночей."),
  earlyDeparturePenaltyNights: int(0, 30, "От 0 до 30 ночей."),
  rules: text(5000),
  version: z.coerce.number().int(),
});

export async function saveSettingsAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = settingsSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, SETTINGS_FIELDS);
  const s = parsed.data;
  const body = {
    checkInTime: s.checkInTime,
    checkOutTime: s.checkOutTime,
    noShowCutoffTime: s.noShowCutoffTime,
    prepaymentPercent: s.prepaymentPercent,
    paymentHoldMinutes: s.paymentHoldMinutes,
    pendingHoldHours: s.pendingHoldHours,
    autoConfirmWebBookings: checkbox(form, "autoConfirmWebBookings"),
    autoReleaseRoomAfterCleaning: checkbox(form, "autoReleaseRoomAfterCleaning"),
    requireEmailConfirmation: checkbox(form, "requireEmailConfirmation"),
    cancellationPolicy: {
      freeCancelHoursBeforeCheckIn: s.freeCancelHoursBeforeCheckIn,
      lateCancelPenaltyNights: s.lateCancelPenaltyNights,
      noShowPenaltyNights: s.noShowPenaltyNights,
      earlyDeparturePenaltyNights: s.earlyDeparturePenaltyNights,
    },
    rules: s.rules,
    version: s.version,
  };
  const result = await api("/hotel/settings", { method: "PUT", body });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, SETTINGS_FIELDS);
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
