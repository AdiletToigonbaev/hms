"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { api } from "@/lib/api/server";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import type { PermissionOverride, Staff } from "@/lib/api/types";
import { createStaffSchema, fieldErrors, PASSWORD_MIN } from "@/lib/validation/auth";

// Каждое действие — отдельная точка входа (прямой POST), поэтому права проверяет API на каждом вызове.

// Поля, которые возвращаются в форму при ошибке (пароли — никогда).
const STAFF_FIELDS = ["fullName", "email", "phone", "role"] as const;

function handleAuth(status: number) {
  if (status === 401) redirect("/login");
}

export async function createStaffAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = createStaffSchema.safeParse({
    fullName: form.get("fullName"),
    email: form.get("email") ?? "",
    phone: form.get("phone") ?? "",
    role: form.get("role"),
    initialPassword: form.get("initialPassword"),
  });
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, STAFF_FIELDS);

  const result = await api<Staff>("/staff", { method: "POST", body: parsed.data });
  if (!result.ok) {
    handleAuth(result.status);
    return withValues(toFormState(result.problem), form, STAFF_FIELDS);
  }
  redirect(`/owner/staff/${result.data.id}`);
}

const updateSchema = z.object({
  id: z.string().uuid(),
  fullName: z.string().trim().min(2, "Укажите ФИО.").max(200),
  email: z.string().trim(),
  phone: z.string().trim(),
});

export async function updateStaffAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = updateSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, STAFF_FIELDS);
  const { id, ...data } = parsed.data;

  const result = await api<Staff>(`/staff/${id}`, {
    method: "PUT",
    body: { fullName: data.fullName, email: data.email || null, phone: data.phone || null },
  });
  if (!result.ok) {
    handleAuth(result.status);
    return withValues(toFormState(result.problem), form, STAFF_FIELDS);
  }
  revalidatePath(`/owner/staff/${id}`);
  return { ok: true };
}

export async function setActiveAction(_: FormState, form: FormData): Promise<FormState> {
  const id = z.string().uuid().parse(form.get("id"));
  const activate = form.get("activate") === "true";
  const result = await api<Staff>(`/staff/${id}/${activate ? "activate" : "deactivate"}`, { method: "POST" });
  if (!result.ok) {
    handleAuth(result.status);
    return toFormState(result.problem);
  }
  revalidatePath(`/owner/staff/${id}`);
  return { ok: true };
}

const resetSchema = z.object({
  id: z.string().uuid(),
  newPassword: z.string().min(PASSWORD_MIN, `Пароль должен содержать не менее ${PASSWORD_MIN} символов.`).max(128),
});

export async function resetStaffPasswordAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = resetSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return { ok: false, fieldErrors: fieldErrors(parsed.error) };

  const result = await api(`/staff/${parsed.data.id}/reset-password`, {
    method: "POST",
    body: { newPassword: parsed.data.newPassword },
  });
  if (!result.ok) {
    handleAuth(result.status);
    return toFormState(result.problem);
  }
  revalidatePath(`/owner/staff/${parsed.data.id}`);
  return { ok: true };
}

const overridesSchema = z.array(
  z.object({ code: z.string().min(1), isGranted: z.boolean(), limitAmount: z.number().positive().nullable() }),
);

export async function setPermissionsAction(_: FormState, form: FormData): Promise<FormState> {
  const id = z.string().uuid().parse(form.get("id"));
  const parsed = overridesSchema.safeParse(JSON.parse(String(form.get("overrides") ?? "[]")));
  if (!parsed.success) return { ok: false, error: "validation.failed" };

  const overrides: PermissionOverride[] = parsed.data;
  const result = await api(`/staff/${id}/permissions`, { method: "PUT", body: { overrides } });
  if (!result.ok) {
    handleAuth(result.status);
    return toFormState(result.problem);
  }
  revalidatePath(`/owner/staff/${id}`);
  return { ok: true };
}
