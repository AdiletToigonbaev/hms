"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import type { AutoReplies, MessageTemplate, MessagingSettings } from "@/lib/api/types";
import { fieldErrors } from "@/lib/validation/auth";
import { messagingSettingsSchema, readAutoReplies, templateSchema } from "@/lib/validation/messaging";

// Шаблоны, напоминания и автоответчик — владелец (whatsapp.templates.manage, проверяет API).

const code = z.string().regex(/^[a-z_]{3,50}$/);

function authGuard(status: number) {
  if (status === 401) redirect("/login");
}

export async function saveTemplateAction(_: FormState, form: FormData): Promise<FormState> {
  const fields = ["emailSubject", "emailBody", "whatsAppBody", "whatsAppName", "whatsAppStatus"] as const;
  const templateCode = code.parse(form.get("code"));
  const parsed = templateSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, fields);
  const version = Number(form.get("version"));
  const result = await api<MessageTemplate>(`/messaging/templates/${templateCode}/ru`, {
    method: "PUT",
    body: { ...parsed.data, version: Number.isFinite(version) ? version : null },
  });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, fields);
  }
  revalidatePath("/owner/messages");
  revalidatePath(`/owner/messages/templates/${templateCode}`);
  return { ok: true };
}

export async function saveMessagingSettingsAction(_: FormState, form: FormData): Promise<FormState> {
  const fields = ["hourlyStartReminderMinutes", "hourlyEndReminderMinutes", "nightlyStartReminderTime", "nightlyEndReminderMinutes", "greetingText", "fallbackText", "stopText"] as const;
  const parsed = messagingSettingsSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, fields);
  const version = Number(form.get("version"));
  const result = await api<MessagingSettings>("/messaging/settings", {
    method: "PUT",
    body: { ...parsed.data, version: Number.isFinite(version) ? version : null },
  });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, fields);
  }
  revalidatePath("/owner/messages");
  return { ok: true };
}

export async function saveAutoRepliesAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = readAutoReplies(form);
  if (!parsed.success) return { ok: false, error: "validation.failed", message: parsed.error.issues[0]?.message };
  const result = await api<AutoReplies>("/messaging/auto-replies", { method: "PUT", body: { items: parsed.data } });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidatePath("/owner/messages/auto-replies");
  return { ok: true };
}

export interface AutoReplyTestState {
  reply?: string;
  outcome?: string;
  error?: string;
}

export async function testAutoReplyAction(_: AutoReplyTestState, form: FormData): Promise<AutoReplyTestState> {
  const text = String(form.get("text") ?? "").trim().slice(0, 500);
  if (!text) return { error: "Введите сообщение гостя." };
  const result = await api<{ reply: string; outcome: string }>("/messaging/auto-replies/test", {
    method: "POST",
    body: { text, firstContact: form.get("firstContact") === "on" },
  });
  if (!result.ok) {
    authGuard(result.status);
    return { error: result.problem.title ?? "Не удалось проверить ответ." };
  }
  return result.data;
}
