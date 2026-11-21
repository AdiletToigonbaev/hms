"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import type { Booking, ExtensionQuote, RescheduleQuote } from "@/lib/api/types";
import { checkbox, fieldErrors } from "@/lib/validation/auth";
import {
  cancelSchema,
  createBookingSchema,
  createHourlyBookingSchema,
  extendSchema,
  rescheduleSchema,
  updateBookingSchema,
  type RescheduleInput,
} from "@/lib/validation/bookings";

// Каждое действие — отдельная точка входа (прямой POST): права проверяет API на каждом вызове.
// Стоимость в браузере не считается — её рассчитывает и фиксирует API.

const uuid = z.string().uuid();
const idempotencyKey = z.string().regex(/^[A-Za-z0-9_-]{8,100}$/);
const CREATE_FIELDS = ["guestName", "guestPhone", "guestEmail", "comment", "promoCode"] as const;
const UPDATE_FIELDS = ["guestName", "guestPhone", "guestEmail", "comment", "adults", "children"] as const;

function authGuard(status: number) {
  if (status === 401) redirect("/login");
}

export async function createBookingAction(_: FormState, form: FormData): Promise<FormState> {
  const hourly = form.get("kind") === "Hourly";
  const parsed = (hourly ? createHourlyBookingSchema : createBookingSchema).safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, CREATE_FIELDS);

  const key = idempotencyKey.safeParse(form.get("idempotencyKey"));
  if (!key.success) return { ok: false, error: "idempotency.key_required" };

  const services = form
    .getAll("serviceIds")
    .map(String)
    .filter((v) => uuid.safeParse(v).success)
    .map((serviceId) => ({ serviceId, quantity: 1 }));

  const result = await api<Booking>("/bookings", {
    method: "POST",
    headers: { "Idempotency-Key": key.data },
    body: { ...parsed.data, kind: hourly ? "Hourly" : "Nightly", services, confirm: checkbox(form, "confirm") },
  });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, CREATE_FIELDS);
  }
  revalidatePath("/admin/bookings");
  redirect(`/admin/bookings/${result.data.id}?created=1`);
}

export async function updateBookingAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const parsed = updateBookingSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, UPDATE_FIELDS);

  const result = await api<Booking>(`/bookings/${id}`, {
    method: "PUT",
    body: { ...parsed.data, version: form.get("version") ? Number(form.get("version")) : null },
  });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, UPDATE_FIELDS);
  }
  revalidatePath(`/admin/bookings/${id}`);
  return { ok: true };
}

export async function confirmBookingAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const result = await api<Booking>(`/bookings/${id}/confirm`, { method: "POST" });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidatePath(`/admin/bookings/${id}`);
  return { ok: true };
}

/** Расчёт продления для окна подтверждения: новое время выезда и доплата (считает API). */
export async function quoteExtensionAction(id: string, units: number): Promise<{ ok: true; quote: ExtensionQuote } | { ok: false; state: FormState }> {
  const bookingId = uuid.parse(id);
  const parsed = extendSchema.safeParse({ units });
  if (!parsed.success) return { ok: false, state: { ok: false, fieldErrors: fieldErrors(parsed.error) } };
  const result = await api<ExtensionQuote>(`/bookings/${bookingId}/extend/quote?units=${parsed.data.units}`);
  if (!result.ok) {
    authGuard(result.status);
    return { ok: false, state: toFormState(result.problem) };
  }
  return { ok: true, quote: result.data };
}

export async function extendBookingAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const parsed = extendSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, ["units"]);
  const version = form.get("version") ? Number(form.get("version")) : null;
  const result = await api<Booking>(`/bookings/${id}/extend`, { method: "POST", body: { units: parsed.data.units, version } });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, ["units"]);
  }
  revalidatePath(`/admin/bookings/${id}`);
  return { ok: true };
}

export async function cancelBookingAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const parsed = cancelSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, ["reason"]);

  const result = await api<Booking>(`/bookings/${id}/cancel`, {
    method: "POST",
    body: { ...parsed.data, waivePenalty: checkbox(form, "waivePenalty") },
  });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, ["reason"]);
  }
  revalidatePath(`/admin/bookings/${id}`);
  revalidatePath("/admin/calendar");
  return { ok: true };
}

/** Неявка: гость не пришёл — номер освобождается, начисляется штраф по правилам (снять — только с правом). */
export async function noShowBookingAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const version = form.get("version") ? Number(form.get("version")) : null;
  const result = await api<Booking>(`/bookings/${id}/no-show`, {
    method: "POST",
    body: { waivePenalty: checkbox(form, "waivePenalty"), version },
  });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidatePath(`/admin/bookings/${id}`);
  revalidatePath("/admin/calendar");
  return { ok: true };
}

type RescheduleResult<T> = { ok: true; data: T } | { ok: false; state: FormState };

function rescheduleBody(input: unknown): { ok: true; body: RescheduleInput } | { ok: false; state: FormState } {
  const parsed = rescheduleSchema.safeParse(input);
  return parsed.success ? { ok: true, body: parsed.data } : { ok: false, state: { ok: false, fieldErrors: fieldErrors(parsed.error) } };
}

/** Расчёт переноса для окна подтверждения: новое время, номер и цена по текущим ценам (считает API). */
export async function quoteRescheduleAction(id: string, input: RescheduleInput): Promise<RescheduleResult<RescheduleQuote>> {
  const bookingId = uuid.parse(id);
  const body = rescheduleBody(input);
  if (!body.ok) return body;
  const result = await api<RescheduleQuote>(`/bookings/${bookingId}/reschedule/quote`, { method: "POST", body: body.body });
  if (!result.ok) {
    authGuard(result.status);
    return { ok: false, state: toFormState(result.problem) };
  }
  return { ok: true, data: result.data };
}

/** Перенос брони (календарь или форма в карточке). Версия брони защищает от одновременной правки. */
export async function rescheduleBookingAction(id: string, input: RescheduleInput, version: number): Promise<RescheduleResult<Booking>> {
  const bookingId = uuid.parse(id);
  const body = rescheduleBody(input);
  if (!body.ok) return body;
  const result = await api<Booking>(`/bookings/${bookingId}/reschedule`, {
    method: "POST",
    body: { ...body.body, version: Number.isFinite(version) ? version : null },
  });
  if (!result.ok) {
    authGuard(result.status);
    return { ok: false, state: toFormState(result.problem) };
  }
  revalidatePath(`/admin/bookings/${bookingId}`);
  revalidatePath("/admin/calendar");
  revalidatePath("/admin/bookings");
  return { ok: true, data: result.data };
}
