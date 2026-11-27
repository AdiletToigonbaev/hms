"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import type { Conversation, Message } from "@/lib/api/types";
import { fieldErrors } from "@/lib/validation/auth";
import { sendMessageSchema } from "@/lib/validation/messaging";

// Переписка с гостями. Права (whatsapp.send), окно 24 ч и согласие проверяет API на каждом вызове.

const uuid = z.string().uuid();
const idempotencyKey = z.string().regex(/^[A-Za-z0-9_-]{8,100}$/);

function authGuard(status: number) {
  if (status === 401) redirect("/login");
}

function refresh(m: { guestId?: string | null; bookingId?: string | null; conversationId?: string | null }) {
  revalidatePath("/admin/messages");
  if (m.conversationId) revalidatePath(`/admin/messages/${m.conversationId}`);
  if (m.guestId) revalidatePath(`/admin/guests/${m.guestId}`);
  if (m.bookingId) revalidatePath(`/admin/bookings/${m.bookingId}`);
}

export async function sendMessageAction(_: FormState, form: FormData): Promise<FormState> {
  const fields = ["text", "subject", "channel"] as const;
  const parsed = sendMessageSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, fields);
  const key = idempotencyKey.safeParse(form.get("idempotencyKey"));
  if (!key.success) return { ok: false, error: "idempotency.key_required" };

  const result = await api<Message>("/messages", { method: "POST", headers: { "Idempotency-Key": key.data }, body: parsed.data });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, fields);
  }
  refresh(parsed.data);
  return { ok: true };
}

export async function retryMessageAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const result = await api<Message>(`/messages/${id}/retry`, { method: "POST" });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  refresh({ guestId: result.data.guestId, bookingId: result.data.bookingId });
  return { ok: true };
}

export async function resolveConversationAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const result = await api<Conversation>(`/whatsapp/conversations/${id}/resolve`, { method: "POST" });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  refresh({ conversationId: id, guestId: result.data.guestId });
  return { ok: true };
}
