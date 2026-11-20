"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import type { Booking } from "@/lib/api/types";
import { checkbox, fieldErrors } from "@/lib/validation/auth";
import { cancelSchema, createBookingSchema, updateBookingSchema } from "@/lib/validation/bookings";

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
  const parsed = createBookingSchema.safeParse(Object.fromEntries(form));
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
    body: { ...parsed.data, services, confirm: checkbox(form, "confirm") },
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

export async function cancelBookingAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const parsed = cancelSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, ["reason"]);

  const result = await api<Booking>(`/bookings/${id}/cancel`, { method: "POST", body: parsed.data });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, ["reason"]);
  }
  revalidatePath(`/admin/bookings/${id}`);
  return { ok: true };
}
