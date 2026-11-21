"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import type { Expense, ExpenseCategory, PaymentDetails, Refund } from "@/lib/api/types";
import { fieldErrors } from "@/lib/validation/auth";
import {
  categorySchema,
  expenseSchema,
  paymentSchema,
  refundDecisionSchema,
  refundRequestSchema,
  rejectSchema,
  reverseSchema,
  voidSchema,
} from "@/lib/validation/finance";

// Финансовые действия. Права проверяет API на каждом вызове; суммы и остатки считает API.
// Операции, которые нельзя выполнить дважды (платёж, аннулирование, возврат, выплата, расход), — с ключом идемпотентности:
// повтор (двойной клик, обрыв сети) не создаст вторую запись.

const uuid = z.string().uuid();
const idempotencyKey = z.string().regex(/^[A-Za-z0-9_-]{8,100}$/);

function authGuard(status: number) {
  if (status === 401) redirect("/login");
}

function keyOf(form: FormData): string | null {
  const key = idempotencyKey.safeParse(form.get("idempotencyKey"));
  return key.success ? key.data : null;
}

function refreshFinance(bookingId?: string | null) {
  if (bookingId) revalidatePath(`/admin/bookings/${bookingId}`);
  revalidatePath("/admin/payments");
  revalidatePath("/admin/refunds");
}

export async function registerPaymentAction(_: FormState, form: FormData): Promise<FormState> {
  const fields = ["amount", "comment", "type", "method"] as const;
  const parsed = paymentSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, fields);
  const key = keyOf(form);
  if (!key) return { ok: false, error: "idempotency.key_required" };

  const result = await api<PaymentDetails>("/payments/manual", {
    method: "POST",
    headers: { "Idempotency-Key": key },
    body: { ...parsed.data, bookingId: parsed.data.bookingId ?? null },
  });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, fields);
  }
  refreshFinance(parsed.data.bookingId);
  return { ok: true };
}

export async function reversePaymentAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const parsed = reverseSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, ["reason"]);
  const key = keyOf(form);
  if (!key) return { ok: false, error: "idempotency.key_required" };

  const result = await api<PaymentDetails>(`/payments/${id}/reverse`, { method: "POST", headers: { "Idempotency-Key": key }, body: parsed.data });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, ["reason"]);
  }
  refreshFinance(result.data.payment.bookingId);
  return { ok: true };
}

export async function requestRefundAction(_: FormState, form: FormData): Promise<FormState> {
  const fields = ["amount", "reason", "method"] as const;
  const parsed = refundRequestSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, fields);
  const key = keyOf(form);
  if (!key) return { ok: false, error: "idempotency.key_required" };

  const result = await api<Refund>("/refunds", { method: "POST", headers: { "Idempotency-Key": key }, body: parsed.data });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, fields);
  }
  refreshFinance(result.data.bookingId);
  return { ok: true };
}

export async function approveRefundAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const parsed = refundDecisionSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return { ok: false, fieldErrors: fieldErrors(parsed.error) };
  const result = await api<Refund>(`/refunds/${id}/approve`, { method: "POST", body: parsed.data });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  refreshFinance(result.data.bookingId);
  return { ok: true };
}

export async function rejectRefundAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const parsed = rejectSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, ["comment"]);
  const result = await api<Refund>(`/refunds/${id}/reject`, { method: "POST", body: parsed.data });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, ["comment"]);
  }
  refreshFinance(result.data.bookingId);
  return { ok: true };
}

export async function markRefundPaidAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const key = keyOf(form);
  if (!key) return { ok: false, error: "idempotency.key_required" };
  const result = await api<Refund>(`/refunds/${id}/mark-paid`, { method: "POST", headers: { "Idempotency-Key": key }, body: {} });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  refreshFinance(result.data.bookingId);
  return { ok: true };
}

// ---------- Расходы ----------

const EXPENSE_FIELDS = ["categoryId", "amount", "date", "method", "supplier", "description"] as const;

export async function saveExpenseAction(_: FormState, form: FormData): Promise<FormState> {
  const id = form.get("id") ? uuid.parse(form.get("id")) : null;
  const parsed = expenseSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, EXPENSE_FIELDS);

  let result;
  if (id) {
    result = await api<Expense>(`/expenses/${id}`, { method: "PUT", body: { ...parsed.data, version: Number(form.get("version")) || null } });
  } else {
    const key = keyOf(form);
    if (!key) return { ok: false, error: "idempotency.key_required" };
    result = await api<Expense>("/expenses", { method: "POST", headers: { "Idempotency-Key": key }, body: { ...parsed.data, version: null } });
  }
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, EXPENSE_FIELDS);
  }
  revalidatePath("/admin/expenses");
  if (id) revalidatePath(`/admin/expenses/${id}`);
  return { ok: true };
}

export async function voidExpenseAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const parsed = voidSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, ["reason"]);
  const result = await api<Expense>(`/expenses/${id}/void`, { method: "POST", body: parsed.data });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, ["reason"]);
  }
  revalidatePath("/admin/expenses");
  revalidatePath(`/admin/expenses/${id}`);
  return { ok: true };
}

export async function saveCategoryAction(_: FormState, form: FormData): Promise<FormState> {
  const id = form.get("id") ? uuid.parse(form.get("id")) : null;
  const parsed = categorySchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, ["name"]);
  const result = await api<ExpenseCategory>(id ? `/expense-categories/${id}` : "/expense-categories", {
    method: id ? "PUT" : "POST",
    body: parsed.data,
  });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, ["name"]);
  }
  revalidatePath("/admin/expenses");
  return { ok: true };
}

export async function toggleCategoryAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const active = form.get("active") === "true";
  const result = await api<ExpenseCategory>(`/expense-categories/${id}/${active ? "activate" : "deactivate"}`, { method: "POST" });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidatePath("/admin/expenses");
  return { ok: true };
}
