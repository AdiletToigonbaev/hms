"use server";

import { revalidatePath } from "next/cache";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { z } from "zod";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import type { ExtensionQuote, Me, MyBooking, MyRefund, PortalConsents } from "@/lib/api/types";
import { allSessionCookies } from "@/lib/auth/cookies";
import { checkbox, fieldErrors } from "@/lib/validation/auth";

// Кабинет гостя. Каждое действие — отдельный вызов API: владение бронью и права проверяет API (чужая бронь — 404).

const uuid = z.string().uuid();
const idempotencyKey = z.string().regex(/^[A-Za-z0-9_-]{8,100}$/);

function authGuard(status: number) {
  if (status === 401) redirect("/login");
}

function refresh(id?: string) {
  revalidatePath("/account");
  revalidatePath("/account/payments");
  if (id) revalidatePath(`/account/bookings/${id}`);
}

export async function cancelMyBookingAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const reason = String(form.get("reason") ?? "").trim().slice(0, 500);
  const result = await api<MyBooking>(`/me/bookings/${id}/cancel`, { method: "POST", body: { reason: reason || null } });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  refresh(id);
  return { ok: true };
}

/** Расчёт продления для окна подтверждения (доплата по текущим ценам считает API). */
export async function quoteMyExtensionAction(id: string, units: number): Promise<{ ok: true; quote: ExtensionQuote } | { ok: false; state: FormState }> {
  const parsed = z.number().int().min(1).max(30).safeParse(units);
  if (!parsed.success) return { ok: false, state: { ok: false, error: "validation_failed" } };
  const result = await api<ExtensionQuote>(`/me/bookings/${uuid.parse(id)}/extend/quote?units=${parsed.data}`);
  if (!result.ok) {
    authGuard(result.status);
    return { ok: false, state: toFormState(result.problem) };
  }
  return { ok: true, quote: result.data };
}

export async function extendMyBookingAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const units = z.coerce.number().int().min(1).max(30).safeParse(form.get("units"));
  if (!units.success) return { ok: false, fieldErrors: { units: "От 1 до 30." } };
  const key = idempotencyKey.safeParse(form.get("idempotencyKey"));
  if (!key.success) return { ok: false, error: "idempotency.key_required" };
  const version = form.get("version") ? Number(form.get("version")) : null;
  const result = await api<MyBooking>(`/me/bookings/${id}/extend`, {
    method: "POST",
    headers: { "Idempotency-Key": key.data },
    body: { units: units.data, version },
  });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  refresh(id);
  return { ok: true };
}

const refundSchema = z.object({
  bookingId: z.string().uuid(),
  amount: z.coerce.number({ message: "Укажите сумму." }).positive("Сумма должна быть больше нуля.").multipleOf(0.01, "Не больше двух знаков после запятой."),
  reason: z.string().trim().min(1, "Укажите причину возврата.").max(500),
});

export async function requestMyRefundAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = refundSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, ["amount", "reason"]);
  const key = idempotencyKey.safeParse(form.get("idempotencyKey"));
  if (!key.success) return { ok: false, error: "idempotency.key_required" };
  const result = await api<MyRefund[]>("/me/refunds", { method: "POST", headers: { "Idempotency-Key": key.data }, body: parsed.data });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, ["amount", "reason"]);
  }
  refresh(parsed.data.bookingId);
  return { ok: true };
}

const profileSchema = z.object({
  fullName: z.string().trim().min(1, "Укажите имя.").max(200),
  phone: z
    .string()
    .trim()
    .transform((v) => (v === "" ? null : v))
    .pipe(z.string().regex(/^\+?[\d\s()-]{7,20}$/, "Некорректный номер телефона. Пример: +996 555 123 456.").nullable()),
});

export async function updateProfileAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = profileSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, ["fullName", "phone"]);
  const result = await api<Me>("/me", { method: "PUT", body: parsed.data });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, ["fullName", "phone"]);
  }
  revalidatePath("/account/profile");
  return { ok: true };
}

export async function setConsentAction(_: FormState, form: FormData): Promise<FormState> {
  const result = await api<PortalConsents>("/me/consents", { method: "PUT", body: { whatsApp: checkbox(form, "whatsApp") } });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidatePath("/account/profile");
  return { ok: true };
}

/** Удаление аккаунта: API обезличивает карточку и брони и удаляет аккаунт; здесь — выход и переход на главную. */
export async function deleteAccountAction(_: FormState, form: FormData): Promise<FormState> {
  const password = String(form.get("password") ?? "");
  if (!password) return { ok: false, fieldErrors: { password: "Введите пароль для подтверждения." } };
  const result = await api<void>("/me", { method: "DELETE", body: { password } });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  const jar = await cookies();
  for (const name of allSessionCookies) jar.delete(name);
  redirect("/?accountDeleted=1");
}
