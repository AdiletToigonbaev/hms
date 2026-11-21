"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { toFormState, type FormState } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import type { ChecklistTemplate, HousekeepingTask, MaintenanceRequest } from "@/lib/api/types";
import { checkbox, fieldErrors } from "@/lib/validation/auth";

// Уборка: задачи (администратор и уборщик), чек-лист, заявки о неисправностях.
// Каждое действие — отдельная точка входа: права и владение задачей проверяет API.

const uuid = z.string().uuid();
const idempotencyKey = z.string().regex(/^[A-Za-z0-9_-]{8,100}$/);

const createTaskSchema = z.object({
  roomId: z.string().uuid({ message: "Выберите номер." }),
  priority: z.enum(["Normal", "High"]),
  notes: z.string().trim().max(500, "Не длиннее 500 символов.").optional(),
});

const maintenanceSchema = z.object({
  roomId: z.string().uuid({ message: "Выберите номер." }),
  type: z.enum(["Defect", "Damage"], { message: "Выберите тип." }),
  severity: z.enum(["Low", "Normal", "High", "Critical"], { message: "Выберите срочность." }),
  description: z.string().trim().min(1, "Опишите, что случилось.").max(1000, "Не длиннее 1000 символов."),
});

function authGuard(status: number) {
  if (status === 401) redirect("/login");
}

function refresh() {
  revalidatePath("/admin/housekeeping");
  revalidatePath("/hk");
  revalidatePath("/admin/rooms");
}

async function call<T>(path: string, init: Parameters<typeof api>[1]): Promise<FormState> {
  const result = await api<T>(path, init);
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  refresh();
  return { ok: true };
}

export async function createTaskAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = createTaskSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return { ok: false, fieldErrors: fieldErrors(parsed.error) };
  return call<HousekeepingTask>("/housekeeping/tasks", { method: "POST", body: { ...parsed.data, notes: parsed.data.notes || null, dueAt: null } });
}

export async function assignTaskAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const userId = uuid.safeParse(form.get("userId"));
  if (!userId.success) return { ok: false, fieldErrors: { userId: "Выберите уборщика." } };
  return call<HousekeepingTask>(`/housekeeping/tasks/${id}/assign`, { method: "POST", body: { userId: userId.data } });
}

export async function deleteTaskAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  return call<void>(`/housekeeping/tasks/${id}`, { method: "DELETE" });
}

export async function inspectTaskAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const approved = form.get("decision") === "approve";
  const reason = String(form.get("reason") ?? "").trim();
  if (!approved && !reason) return { ok: false, fieldErrors: { reason: "Укажите, что нужно исправить." } };
  return call<HousekeepingTask>(`/housekeeping/tasks/${id}/inspect`, { method: "POST", body: { approved, reason: reason || null } });
}

/** Действия уборщика: взять задачу, начать, завершить. */
export async function taskStepAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const step = z.enum(["take", "start", "complete"]).parse(form.get("step"));
  return call<HousekeepingTask>(`/housekeeping/tasks/${id}/${step}`, { method: "POST" });
}

/** Отметка пункта чек-листа — сразу, без перезагрузки страницы (ответ — обновлённая задача). */
export async function setChecklistItemAction(id: string, itemId: string, done: boolean): Promise<{ ok: true; task: HousekeepingTask } | { ok: false; state: FormState }> {
  const result = await api<HousekeepingTask>(`/housekeeping/tasks/${uuid.parse(id)}/checklist/${uuid.parse(itemId)}`, { method: "PUT", body: { done } });
  if (!result.ok) {
    authGuard(result.status);
    return { ok: false, state: toFormState(result.problem) };
  }
  return { ok: true, task: result.data };
}

export async function saveChecklistTemplateAction(_: FormState, form: FormData): Promise<FormState> {
  const items = String(form.get("items") ?? "")
    .split("\n")
    .map((s) => s.trim())
    .filter(Boolean);
  if (items.length === 0 || items.length > 40) return { ok: false, fieldErrors: { items: "В чек-листе — от 1 до 40 пунктов." } };
  if (items.some((i) => i.length > 200)) return { ok: false, fieldErrors: { items: "Пункт — не длиннее 200 символов." } };
  const version = form.get("version") ? Number(form.get("version")) : null;
  return call<ChecklistTemplate>("/housekeeping/checklist-template", { method: "PUT", body: { items, version } });
}

export async function createMaintenanceAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = maintenanceSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return { ok: false, fieldErrors: fieldErrors(parsed.error) };
  const key = idempotencyKey.safeParse(form.get("idempotencyKey"));
  if (!key.success) return { ok: false, error: "idempotency.key_required" };
  const taskId = uuid.safeParse(form.get("taskId"));
  return call<MaintenanceRequest>("/maintenance-requests", {
    method: "POST",
    headers: { "Idempotency-Key": key.data },
    body: { ...parsed.data, taskId: taskId.success ? taskId.data : null },
  });
}

export async function updateMaintenanceAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const status = z.enum(["New", "InProgress", "Resolved", "Rejected"]).parse(form.get("status"));
  const note = String(form.get("note") ?? "").trim();
  if (status === "Rejected" && !note) return { ok: false, fieldErrors: { note: "Укажите, почему заявка отклонена." } };
  const version = form.get("version") ? Number(form.get("version")) : null;
  return call<MaintenanceRequest>(`/maintenance-requests/${id}`, {
    method: "PUT",
    body: { status, note: note || null, closeRoom: checkbox(form, "closeRoom"), version },
  });
}
