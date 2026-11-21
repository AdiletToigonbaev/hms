"use client";

import { useTranslations } from "next-intl";
import { useActionState, useEffect, useRef, useState } from "react";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { DateRangePicker } from "@/components/forms/date-range-picker";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { SelectField } from "@/components/forms/native-select";
import { SubmitButton } from "@/components/forms/submit-button";
import type { FormState } from "@/lib/api/problem";
import type { Expense, ExpenseCategory, PaymentItem, PaymentType, Refund } from "@/lib/api/types";
import { paymentMethodLabel, paymentMethods, paymentTypeLabel, registrableTypes } from "@/lib/finance/labels";
import { formatMoney } from "@/lib/format";
import {
  approveRefundAction,
  markRefundPaidAction,
  registerPaymentAction,
  rejectRefundAction,
  requestRefundAction,
  reversePaymentAction,
  saveCategoryAction,
  saveExpenseAction,
  toggleCategoryAction,
  voidExpenseAction,
} from "./actions";

function newKey() {
  return crypto.randomUUID().replace(/-/g, "");
}

/**
 * Ключ идемпотентности формы: живёт, пока операция не завершилась. Повтор (двойной клик, обрыв сети) уходит
 * с тем же ключом и не создаёт вторую запись; после ответа API — новый ключ для следующей операции.
 */
function useIdempotencyKey(state: FormState) {
  const [key, setKey] = useState(newKey);
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (state.ok !== undefined) setKey(newKey());
  }, [state]);
  return key;
}

function amountText(value: number) {
  return value.toFixed(2).replace(".", ",").replace(",00", "");
}

/** Приём оплаты по брони: сумма по умолчанию — остаток к оплате; больше остатка API не примет. */
export function RegisterPaymentForm({ bookingId, balance, defaultType }: { bookingId: string; balance: number; defaultType: PaymentType }) {
  const t = useTranslations("Finance");
  const [state, action, pending] = useActionState<FormState, FormData>(registerPaymentAction, {});
  const key = useIdempotencyKey(state);
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="idempotencyKey" value={key} />
      <input type="hidden" name="bookingId" value={bookingId} />
      <FormAlert state={state} success={t("paymentRegistered")} />
      <div className="grid gap-3 sm:grid-cols-2">
        <Field
          key={`${balance}-${state.ok}`}
          name="amount"
          inputMode="decimal"
          label={t("amount")}
          required
          defaultValue={v.amount ?? amountText(balance)}
          error={e.amount}
          hint={t("amountHint", { balance: formatMoney(balance) })}
        />
        <SelectField name="method" id="pay-method" label={t("method")} defaultValue={v.method ?? "Cash"} error={e.method}>
          {paymentMethods.map((m) => (
            <option key={m} value={m}>
              {paymentMethodLabel[m]}
            </option>
          ))}
        </SelectField>
      </div>
      <SelectField name="type" id="pay-type" label={t("type")} defaultValue={v.type ?? defaultType} error={e.type}>
        {registrableTypes.map((type) => (
          <option key={type} value={type}>
            {paymentTypeLabel[type]}
          </option>
        ))}
      </SelectField>
      <Field name="comment" label={t("comment")} defaultValue={v.comment} error={e.comment} hint={t("commentHint")} />
      <div>
        <SubmitButton pending={pending}>{t("register")}</SubmitButton>
      </div>
    </form>
  );
}

/** Аннулирование ошибочной регистрации — с причиной и окном подтверждения. */
export function ReversePaymentForm({ payment }: { payment: PaymentItem }) {
  const t = useTranslations("Finance");
  const [state, action, pending] = useActionState<FormState, FormData>(reversePaymentAction, {});
  const key = useIdempotencyKey(state);
  const formRef = useRef<HTMLFormElement>(null);
  const [asking, setAsking] = useState(false);
  return (
    <form ref={formRef} action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="idempotencyKey" value={key} />
      <input type="hidden" name="id" value={payment.id} />
      <FormAlert state={state} success={t("reversed")} />
      <p className="text-sm text-muted-foreground">{t("reverseHint")}</p>
      <Field name="reason" label={t("reverseReason")} required defaultValue={state.values?.reason} error={state.fieldErrors?.reason} />
      <div>
        <SubmitButton
          pending={pending}
          variant="destructive"
          onClick={(ev) => {
            const reason = new FormData(formRef.current!).get("reason");
            if (typeof reason === "string" && reason.trim()) {
              ev.preventDefault();
              setAsking(true);
            }
          }}
        >
          {t("reverse")}
        </SubmitButton>
      </div>
      <ConfirmDialog
        open={asking}
        onOpenChange={setAsking}
        title={t("reverseConfirmTitle", { amount: formatMoney(payment.amount) })}
        description={t("reverseConfirmText")}
        confirmLabel={t("reverseYes")}
        cancelLabel={t("no")}
        destructive
        onConfirm={() => formRef.current?.requestSubmit()}
      />
    </form>
  );
}

/** Запрос возврата по платежу: не больше суммы платежа за вычетом уже оформленных возвратов. */
export function RefundRequestForm({ payment, available }: { payment: PaymentItem; available: number }) {
  const t = useTranslations("Finance");
  const [state, action, pending] = useActionState<FormState, FormData>(requestRefundAction, {});
  const key = useIdempotencyKey(state);
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="idempotencyKey" value={key} />
      <input type="hidden" name="paymentId" value={payment.id} />
      <FormAlert state={state} success={t("refundRequested")} />
      <div className="grid gap-3 sm:grid-cols-2">
        <Field
          name="amount"
          inputMode="decimal"
          label={t("refundAmount")}
          required
          defaultValue={v.amount ?? amountText(available)}
          error={e.amount}
          hint={t("refundAvailable", { amount: formatMoney(available) })}
        />
        <SelectField name="method" id={`refund-method-${payment.id}`} label={t("refundMethod")} defaultValue={v.method ?? payment.method} error={e.method}>
          {paymentMethods.map((m) => (
            <option key={m} value={m}>
              {paymentMethodLabel[m]}
            </option>
          ))}
        </SelectField>
      </div>
      <Field name="reason" label={t("refundReason")} required defaultValue={v.reason} error={e.reason} />
      <div>
        <SubmitButton pending={pending} variant="outline">
          {t("requestRefund")}
        </SubmitButton>
      </div>
    </form>
  );
}

/** Решение по возврату (утвердить / отклонить) и отметка о выплате — в пределах лимита (проверяет API). */
export function RefundActions({ refund }: { refund: Refund }) {
  const t = useTranslations("Finance");
  const [approveState, approve, approving] = useActionState<FormState, FormData>(approveRefundAction, {});
  const [rejectState, reject, rejecting] = useActionState<FormState, FormData>(rejectRefundAction, {});
  const [paidState, markPaid, paying] = useActionState<FormState, FormData>(markRefundPaidAction, {});
  const key = useIdempotencyKey(paidState);
  const payRef = useRef<HTMLFormElement>(null);
  const [askingPay, setAskingPay] = useState(false);
  const [rejecting2, setRejecting] = useState(false);

  if (refund.status === "Approved") {
    return (
      <form ref={payRef} action={markPaid} className="grid gap-2">
        <input type="hidden" name="id" value={refund.id} />
        <input type="hidden" name="idempotencyKey" value={key} />
        <FormAlert state={paidState} />
        <div>
          <SubmitButton
            pending={paying}
            size="sm"
            onClick={(ev) => {
              ev.preventDefault();
              setAskingPay(true);
            }}
          >
            {t("markPaid")}
          </SubmitButton>
        </div>
        <ConfirmDialog
          open={askingPay}
          onOpenChange={setAskingPay}
          title={t("markPaidTitle", { amount: formatMoney(refund.amount) })}
          description={t("markPaidText", { method: paymentMethodLabel[refund.method] })}
          confirmLabel={t("markPaidYes")}
          cancelLabel={t("no")}
          onConfirm={() => payRef.current?.requestSubmit()}
        />
      </form>
    );
  }

  if (refund.status !== "Requested") return null;
  return (
    <div className="grid gap-2">
      <FormAlert state={approveState.error ? approveState : rejectState} />
      {!rejecting2 ? (
        <div className="flex flex-wrap gap-2">
          <form action={approve}>
            <input type="hidden" name="id" value={refund.id} />
            <SubmitButton pending={approving} size="sm">
              {t("approve")}
            </SubmitButton>
          </form>
          <button type="button" className="rounded-lg px-2 text-sm text-destructive hover:underline" onClick={() => setRejecting(true)}>
            {t("reject")}
          </button>
        </div>
      ) : (
        <form action={reject} className="grid gap-2" noValidate>
          <input type="hidden" name="id" value={refund.id} />
          <Field name="comment" label={t("rejectReason")} required defaultValue={rejectState.values?.comment} error={rejectState.fieldErrors?.comment} />
          <div className="flex gap-2">
            <SubmitButton pending={rejecting} size="sm" variant="destructive">
              {t("reject")}
            </SubmitButton>
            <button type="button" className="px-2 text-sm text-muted-foreground hover:underline" onClick={() => setRejecting(false)}>
              {t("no")}
            </button>
          </div>
        </form>
      )}
    </div>
  );
}

/** Расход: внести (сотрудник) или изменить (владелец). */
export function ExpenseForm({ categories, today, expense }: { categories: ExpenseCategory[]; today: string; expense?: Expense }) {
  const t = useTranslations("Finance");
  const [state, action, pending] = useActionState<FormState, FormData>(saveExpenseAction, {});
  const key = useIdempotencyKey(state);
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  const active = categories.filter((c) => c.isActive || c.id === expense?.categoryId);
  // После успешного внесения форма очищается (новый ключ — новый расход).
  const resetKey = expense ? "edit" : key;
  return (
    <form key={resetKey} action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="idempotencyKey" value={key} />
      {expense && <input type="hidden" name="id" value={expense.id} />}
      {expense && <input type="hidden" name="version" value={expense.version} />}
      <FormAlert state={state} success={expense ? t("expenseSaved") : t("expenseCreated")} />
      <div className="grid gap-3 sm:grid-cols-2">
        <SelectField name="categoryId" id="expense-category" label={t("category")} defaultValue={v.categoryId ?? expense?.categoryId ?? ""} error={e.categoryId}>
          <option value="" disabled>
            {t("chooseCategory")}
          </option>
          {active.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </SelectField>
        <Field name="amount" inputMode="decimal" label={t("amount")} required defaultValue={v.amount ?? (expense ? amountText(expense.amount) : "")} error={e.amount} />
        <DateRangePicker mode="single" size="compact" startName="date" defaultStart={v.date ?? expense?.date ?? today} today={today} startLabel={t("date")} />
        <SelectField name="method" id="expense-method" label={t("method")} defaultValue={v.method ?? expense?.method ?? "Cash"} error={e.method}>
          {paymentMethods.map((m) => (
            <option key={m} value={m}>
              {paymentMethodLabel[m]}
            </option>
          ))}
        </SelectField>
      </div>
      {e.date && (
        <p className="text-sm text-destructive" role="alert">
          {e.date}
        </p>
      )}
      <Field name="description" label={t("description")} required defaultValue={v.description ?? expense?.description} error={e.description} hint={t("descriptionHint")} />
      <Field name="supplier" label={t("supplier")} defaultValue={v.supplier ?? expense?.supplier ?? ""} error={e.supplier} />
      <div>
        <SubmitButton pending={pending}>{expense ? t("save") : t("addExpense")}</SubmitButton>
      </div>
    </form>
  );
}

/** Аннулирование расхода (владелец) — с причиной и подтверждением; запись остаётся в истории. */
export function VoidExpenseForm({ expense }: { expense: Expense }) {
  const t = useTranslations("Finance");
  const [state, action, pending] = useActionState<FormState, FormData>(voidExpenseAction, {});
  const formRef = useRef<HTMLFormElement>(null);
  const [asking, setAsking] = useState(false);
  return (
    <form ref={formRef} action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="id" value={expense.id} />
      <FormAlert state={state} success={t("expenseVoided")} />
      <Field name="reason" label={t("voidReason")} required defaultValue={state.values?.reason} error={state.fieldErrors?.reason} />
      <div>
        <SubmitButton
          pending={pending}
          variant="destructive"
          onClick={(ev) => {
            const reason = new FormData(formRef.current!).get("reason");
            if (typeof reason === "string" && reason.trim()) {
              ev.preventDefault();
              setAsking(true);
            }
          }}
        >
          {t("void")}
        </SubmitButton>
      </div>
      <ConfirmDialog
        open={asking}
        onOpenChange={setAsking}
        title={t("voidConfirmTitle", { amount: formatMoney(expense.amount) })}
        description={t("voidConfirmText")}
        confirmLabel={t("voidYes")}
        cancelLabel={t("no")}
        destructive
        onConfirm={() => formRef.current?.requestSubmit()}
      />
    </form>
  );
}

/** Категории расходов (владелец): добавить, отключить/включить. */
export function CategoryManager({ categories }: { categories: ExpenseCategory[] }) {
  const t = useTranslations("Finance");
  const [state, action, pending] = useActionState<FormState, FormData>(saveCategoryAction, {});
  const [toggleState, toggle] = useActionState<FormState, FormData>(toggleCategoryAction, {});
  return (
    <div className="grid gap-3">
      <FormAlert state={toggleState} />
      <ul className="grid gap-1">
        {categories.map((c) => (
          <li key={c.id} className="flex items-center justify-between gap-2 rounded-lg px-2 py-1 text-sm hover:bg-accent/60">
            <span className={c.isActive ? "" : "text-muted-foreground line-through"}>{c.name}</span>
            <form action={toggle}>
              <input type="hidden" name="id" value={c.id} />
              <input type="hidden" name="active" value={String(!c.isActive)} />
              <button type="submit" className="text-xs text-muted-foreground underline-offset-4 hover:text-foreground hover:underline">
                {c.isActive ? t("deactivate") : t("activate")}
              </button>
            </form>
          </li>
        ))}
      </ul>
      <form key={state.ok ? "saved" : "new"} action={action} className="flex flex-wrap items-end gap-2" noValidate>
        <input type="hidden" name="sortOrder" value="100" />
        <div className="min-w-48 flex-1">
          <Field name="name" label={t("newCategory")} defaultValue={state.ok ? "" : state.values?.name} error={state.fieldErrors?.name} />
        </div>
        <SubmitButton pending={pending} variant="outline">
          {t("add")}
        </SubmitButton>
      </form>
      <FormAlert state={state.error ? state : {}} />
    </div>
  );
}

