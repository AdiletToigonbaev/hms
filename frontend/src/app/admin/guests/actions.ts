"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import type { Guest, GuestLookup } from "@/lib/api/types";
import { fieldErrors } from "@/lib/validation/auth";
import { consentSchema, guestSchema } from "@/lib/validation/guests";

// Каждое действие — отдельная точка входа: права (guest.view / guest.manage / guest.anonymize) проверяет API.

const uuid = z.string().uuid();
const GUEST_FIELDS = ["fullName", "phone", "whatsAppPhone", "email", "birthDate", "notes"] as const;

function authGuard(status: number) {
  if (status === 401) redirect("/login");
}

export async function saveGuestAction(_: FormState, form: FormData): Promise<FormState> {
  const id = form.get("id") ? uuid.parse(form.get("id")) : null;
  const parsed = guestSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, GUEST_FIELDS);

  const body = { ...parsed.data, version: form.get("version") ? Number(form.get("version")) : null };
  const result = await api<Guest>(id ? `/guests/${id}` : "/guests", { method: id ? "PUT" : "POST", body });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, GUEST_FIELDS);
  }
  if (!id) redirect(`/admin/guests/${result.data.id}`);
  revalidatePath(`/admin/guests/${id}`);
  return { ok: true };
}

export async function setConsentAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const parsed = consentSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return { ok: false, fieldErrors: fieldErrors(parsed.error) };
  const result = await api<Guest>(`/guests/${id}/consents`, { method: "POST", body: parsed.data });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidatePath(`/admin/guests/${id}`);
  return { ok: true };
}

export async function anonymizeGuestAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const result = await api<Guest>(`/guests/${id}/anonymize`, { method: "POST" });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidatePath(`/admin/guests/${id}`);
  return { ok: true };
}

/** Поиск гостя при брони (выпадающий список). */
export async function lookupGuestsAction(query: string): Promise<GuestLookup[]> {
  const q = query.trim().slice(0, 100);
  if (q.length < 2) return [];
  const result = await api<GuestLookup[]>(`/guests/lookup?q=${encodeURIComponent(q)}`);
  return result.ok ? result.data : [];
}
