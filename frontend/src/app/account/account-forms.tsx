"use client";

import { useTranslations } from "next-intl";
import { useActionState, useEffect, useRef, useState, useTransition } from "react";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { NativeSelect } from "@/components/forms/native-select";
import { SubmitButton } from "@/components/forms/submit-button";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import type { CancellationQuote, ExtensionQuote, Me, MyBooking, PortalConsents } from "@/lib/api/types";
import { nightsLabel } from "@/lib/bookings/status";
import { formatHotelLocal, formatMoney } from "@/lib/format";
import {
  cancelMyBookingAction,
  deleteAccountAction,
  extendMyBookingAction,
  quoteMyExtensionAction,
  requestMyRefundAction,
  setConsentAction,
  updateProfileAction,
} from "./actions";

const textarea =
  "min-h-20 w-full rounded-lg border border-input bg-card px-2.5 py-2 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive md:text-sm";

function newKey() {
  return crypto.randomUUID().replace(/-/g, "");
}

/** Новый ключ идемпотентности после ответа API: повтор того же нажатия не выполнит действие дважды. */
function useKey(state: FormState) {
  const [key, setKey] = useState(newKey);
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (state.ok !== undefined) setKey(newKey());
  }, [state]);
  return key;
}

/** Отмена брони по правилам гостиницы: условия (бесплатно или штраф) — до подтверждения. */
export function CancelMyBookingForm({ booking, terms }: { booking: MyBooking; terms: CancellationQuote | null }) {
  const t = useTranslations("Account");
  const [state, action, pending] = useActionState<FormState, FormData>(cancelMyBookingAction, {});
  const formRef = useRef<HTMLFormElement>(null);
  const [asking, setAsking] = useState(false);
  const unit = (n: number) => (terms?.unit === "Hour" ? `${n} ч` : nightsLabel(n));
  const termsText = !terms
    ? t("cancelTermsUnknown")
    : terms.isFree
      ? terms.freeUntil
        ? t("cancelFreeUntil", { time: formatHotelLocal(terms.freeUntil) })
        : t("cancelFree")
      : t("cancelPenalty", { amount: formatMoney(terms.penalty), units: unit(terms.units) });
  return (
    <form ref={formRef} action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="id" value={booking.id} />
      <h3 className="font-heading text-lg">{t("cancelTitle")}</h3>
      <FormAlert state={state} />
      <p className="text-sm text-muted-foreground">{termsText}</p>
      <div className="grid gap-1.5">
        <Label htmlFor="reason">{t("cancelReason")}</Label>
        <textarea id="reason" name="reason" maxLength={500} className={textarea} />
      </div>
      <div>
        <SubmitButton
          pending={pending}
          variant="outline"
          className="h-11"
          onClick={(e) => {
            e.preventDefault();
            setAsking(true);
          }}
        >
          {t("cancel")}
        </SubmitButton>
      </div>
      <ConfirmDialog
        open={asking}
        onOpenChange={setAsking}
        title={t("cancelConfirmTitle", { number: booking.number })}
        description={termsText}
        confirmLabel={t("cancelConfirmYes")}
        cancelLabel={t("no")}
        destructive
        onConfirm={() => formRef.current?.requestSubmit()}
      />
    </form>
  );
}

/** Продление: часы или ночи, доплата по текущим ценам — в окне подтверждения. Номер дальше может быть занят — это скажет API. */
export function ExtendMyBookingForm({ booking, maxUnits }: { booking: MyBooking; maxUnits: number }) {
  const t = useTranslations("Account");
  const [state, action, pending] = useActionState<FormState, FormData>(extendMyBookingAction, {});
  const key = useKey(state);
  const formRef = useRef<HTMLFormElement>(null);
  const [units, setUnits] = useState(1);
  const [quote, setQuote] = useState<ExtensionQuote | null>(null);
  const [quoteError, setQuoteError] = useState<FormState>({});
  const [quoting, startQuote] = useTransition();
  const hourly = booking.kind === "Hourly";
  const label = (n: number) => (hourly ? `${n} ч` : nightsLabel(n));
  const ask = () =>
    startQuote(async () => {
      const result = await quoteMyExtensionAction(booking.id, units);
      if (result.ok) {
        setQuote(result.quote);
        setQuoteError({});
      } else {
        setQuoteError(result.state);
      }
    });
  return (
    <form ref={formRef} action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="id" value={booking.id} />
      <input type="hidden" name="version" value={booking.version} />
      <input type="hidden" name="idempotencyKey" value={key} />
      <h3 className="font-heading text-lg">{t("extendTitle")}</h3>
      <FormAlert state={state.ok === false ? state : quoteError} success={state.ok ? t("extended") : undefined} />
      <div className="flex flex-wrap items-end gap-2">
        <div className="grid gap-1.5">
          <Label htmlFor="units">{hourly ? t("extendHours") : t("extendNights")}</Label>
          <NativeSelect id="units" name="units" value={units} onChange={(e) => setUnits(Number(e.target.value))} className="h-11 w-32">
            {Array.from({ length: Math.max(1, maxUnits) }, (_, i) => i + 1).map((n) => (
              <option key={n} value={n}>
                {label(n)}
              </option>
            ))}
          </NativeSelect>
        </div>
        <Button type="button" variant="outline" className="h-11" disabled={quoting || pending} onClick={ask}>
          {t("extend")}
        </Button>
      </div>
      <ConfirmDialog
        open={quote !== null}
        onOpenChange={(open) => !open && setQuote(null)}
        title={t("extendConfirmTitle", { units: label(units) })}
        description={quote ? t("extendConfirmText", { until: formatHotelLocal(quote.newEndsAt), amount: formatMoney(quote.amount) }) : ""}
        confirmLabel={t("extendConfirmYes")}
        cancelLabel={t("no")}
        onConfirm={() => formRef.current?.requestSubmit()}
      />
    </form>
  );
}

/** Запрос возврата переплаты: сумма не больше доступной (проверяет и API); решение принимает администратор. */
export function RefundRequestForm({ bookingId, refundable }: { bookingId: string; refundable: number }) {
  const t = useTranslations("Account");
  const [state, action, pending] = useActionState<FormState, FormData>(requestMyRefundAction, {});
  const key = useKey(state);
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="bookingId" value={bookingId} />
      <input type="hidden" name="idempotencyKey" value={key} />
      <h3 className="font-heading text-lg">{t("refundTitle")}</h3>
      <FormAlert state={state} success={t("refundRequested")} />
      <p className="text-sm text-muted-foreground">{t("refundText", { amount: formatMoney(refundable) })}</p>
      <Field name="amount" label={t("refundAmount")} type="number" inputMode="decimal" step="0.01" min="0.01" max={refundable} defaultValue={v.amount ?? String(refundable)} error={e.amount} className="h-11 sm:max-w-48" />
      <div className="grid gap-1.5">
        <Label htmlFor="refund-reason">{t("refundReason")}</Label>
        <textarea id="refund-reason" name="reason" maxLength={500} defaultValue={v.reason} className={textarea} aria-invalid={e.reason ? true : undefined} />
        {e.reason && <p className="text-xs text-destructive">{e.reason}</p>}
      </div>
      <div>
        <SubmitButton pending={pending} className="h-11">
          {t("refundSend")}
        </SubmitButton>
      </div>
    </form>
  );
}

export function ProfileForm({ me }: { me: Me }) {
  const t = useTranslations("Account");
  const [state, action, pending] = useActionState<FormState, FormData>(updateProfileAction, {});
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid max-w-md gap-4" noValidate>
      <FormAlert state={state} success={t("profileSaved")} />
      <Field name="fullName" label={t("fullName")} autoComplete="name" defaultValue={v.fullName ?? me.fullName} error={e.fullName} className="h-11" />
      <Field name="phone" label={t("phone")} type="tel" autoComplete="tel" placeholder="+996 555 123 456" defaultValue={v.phone ?? me.phone ?? ""} error={e.phone} className="h-11" />
      <p className="text-sm text-muted-foreground">{t("emailIs", { email: me.email ?? "—" })}</p>
      <div>
        <SubmitButton pending={pending} className="h-11">
          {t("save")}
        </SubmitButton>
      </div>
    </form>
  );
}

export function ConsentForm({ consents }: { consents: PortalConsents }) {
  const t = useTranslations("Account");
  const [state, action, pending] = useActionState<FormState, FormData>(setConsentAction, {});
  return (
    <form action={action} className="grid gap-3">
      <FormAlert state={state} success={t("consentSaved")} />
      <p className="text-sm">{consents.personalData ? t("consentPd") : t("consentPdMissing")}</p>
      <label className="flex items-start gap-2 text-sm">
        <Checkbox name="whatsApp" value="true" defaultChecked={consents.whatsApp} className="mt-0.5" />
        <span>{t("consentWhatsApp")}</span>
      </label>
      <div>
        <SubmitButton pending={pending} variant="outline" className="h-11">
          {t("save")}
        </SubmitButton>
      </div>
    </form>
  );
}

/** Удаление аккаунта: пароль и окно подтверждения; при действующих бронях API откажет с понятной причиной. */
export function DeleteAccountForm() {
  const t = useTranslations("Account");
  const [state, action, pending] = useActionState<FormState, FormData>(deleteAccountAction, {});
  const formRef = useRef<HTMLFormElement>(null);
  const [asking, setAsking] = useState(false);
  return (
    <form ref={formRef} action={action} className="grid max-w-md gap-3" noValidate>
      <FormAlert state={state} />
      <p className="text-sm text-muted-foreground">{t("deleteText")}</p>
      <Field name="password" label={t("password")} type="password" autoComplete="current-password" error={state.fieldErrors?.password} className="h-11" />
      <div>
        <SubmitButton
          pending={pending}
          variant="destructive"
          className="h-11"
          onClick={(e) => {
            e.preventDefault();
            setAsking(true);
          }}
        >
          {t("deleteAccount")}
        </SubmitButton>
      </div>
      <ConfirmDialog
        open={asking}
        onOpenChange={setAsking}
        title={t("deleteConfirmTitle")}
        description={t("deleteConfirmText")}
        confirmLabel={t("deleteConfirmYes")}
        cancelLabel={t("no")}
        destructive
        onConfirm={() => formRef.current?.requestSubmit()}
      />
    </form>
  );
}
