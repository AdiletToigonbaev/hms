"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import { maskFromDays } from "@/lib/bookings/status";
import { checkbox, fieldErrors } from "@/lib/validation/auth";
import { discountSchema, rateSchema, serviceSchema } from "@/lib/validation/bookings";

// Каждое действие — отдельная точка входа: права (rate.manage, только владелец) проверяет API.

const uuid = z.string().uuid();
const RATE_FIELDS = ["roomTypeId", "name", "dateFrom", "dateTo", "price", "priority", "unit"] as const;
const DISCOUNT_FIELDS = ["name", "code", "type", "value", "validFrom", "validTo", "minNights", "minHours"] as const;
const SERVICE_FIELDS = ["name", "description", "price"] as const;

function optionalId(form: FormData) {
  return form.get("id") ? uuid.parse(form.get("id")) : null;
}

async function finish(result: { ok: boolean; status: number; problem?: unknown }, form: FormData, fields: readonly string[]): Promise<FormState> {
  if (!result.ok) {
    if (result.status === 401) redirect("/login");
    return withValues(toFormState(result.problem as Parameters<typeof toFormState>[0]), form, fields);
  }
  revalidatePath("/owner/pricing");
  // После сохранения — обратно к списку (сбрасывает режим редактирования).
  redirect("/owner/pricing?saved=1");
}

export async function saveRateAction(_: FormState, form: FormData): Promise<FormState> {
  const id = optionalId(form);
  const parsed = rateSchema.safeParse(Object.fromEntries(form));
  const daysOfWeek = maskFromDays(form.getAll("days").map(String));
  if (!parsed.success || daysOfWeek === 0) {
    const errors = parsed.success ? {} : fieldErrors(parsed.error);
    if (daysOfWeek === 0) errors.days = "Выберите хотя бы один день недели.";
    return withValues({ ok: false, fieldErrors: errors }, form, RATE_FIELDS);
  }
  const body = { ...parsed.data, daysOfWeek, isActive: checkbox(form, "isActive") };
  const result = await api(id ? `/rates/${id}` : "/rates", { method: id ? "PUT" : "POST", body });
  return finish(result, form, RATE_FIELDS);
}

export async function deleteRateAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const result = await api(`/rates/${id}`, { method: "DELETE" });
  return finish(result, form, []);
}

export async function saveDiscountAction(_: FormState, form: FormData): Promise<FormState> {
  const id = optionalId(form);
  const parsed = discountSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, DISCOUNT_FIELDS);
  const result = await api(id ? `/discounts/${id}` : "/discounts", { method: id ? "PUT" : "POST", body: parsed.data });
  return finish(result, form, DISCOUNT_FIELDS);
}

export async function saveServiceAction(_: FormState, form: FormData): Promise<FormState> {
  const id = optionalId(form);
  const parsed = serviceSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, SERVICE_FIELDS);
  const body = { ...parsed.data, isPerNight: checkbox(form, "isPerNight"), isPublic: checkbox(form, "isPublic") };
  const result = await api(id ? `/services/${id}` : "/services", { method: id ? "PUT" : "POST", body });
  return finish(result, form, SERVICE_FIELDS);
}

/** Включение/выключение скидки или услуги. */
export async function toggleActiveAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const kind = z.enum(["discounts", "services"]).parse(form.get("kind"));
  const active = form.get("active") === "true";
  const result = await api(`/${kind}/${id}/${active ? "activate" : "deactivate"}`, { method: "POST" });
  return finish(result, form, []);
}
