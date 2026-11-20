"use client";

import { useTranslations } from "next-intl";
import { useActionState, useEffect, useRef, useState, useTransition } from "react";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Field } from "@/components/forms/field";
import { SelectField } from "@/components/forms/native-select";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import { nightsLabel } from "@/lib/bookings/status";
import type { Booking, ExtensionQuote, ExtraService } from "@/lib/api/types";
import { formatHotelLocal, formatMoney } from "@/lib/format";
import { cancelBookingAction, confirmBookingAction, createBookingAction, extendBookingAction, quoteExtensionAction, updateBookingAction } from "./actions";

const textarea =
  "min-h-20 w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive";

function newKey() {
  return crypto.randomUUID().replace(/-/g, "");
}

export type StayParams =
  | { kind: "Nightly"; roomId: string; checkIn: string; checkOut: string; adults: number; children: number }
  | { kind: "Hourly"; roomId: string; date: string; startHour: number; hours: number; adults: number; children: number };

/**
 * Данные гостя и создание брони. Ключ идемпотентности живёт, пока данные не изменились:
 * повторная отправка (обрыв сети, двойной клик) не создаст вторую бронь. После отказа API ключ обновляется,
 * чтобы исправленный запрос не считался повтором.
 */
export function CreateBookingForm({ stay, services }: { stay: StayParams; services: ExtraService[] }) {
  const t = useTranslations("Bookings");
  const [state, action, pending] = useActionState<FormState, FormData>(createBookingAction, {});
  const [key, setKey] = useState(newKey);
  useEffect(() => {
    // Новый ключ после отказа: синхронизация с результатом серверного действия.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (state.ok === false) setKey(newKey());
  }, [state]);
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  const stayError = e.checkIn ?? e.checkOut ?? e.date ?? e.startHour ?? e.hours ?? e.roomId ?? e.adults ?? e.children;
  return (
    <form action={action} className="grid gap-4" noValidate>
      <input type="hidden" name="idempotencyKey" value={key} />
      {Object.entries(stay).map(([name, value]) => (
        <input key={name} type="hidden" name={name} value={String(value)} />
      ))}
      <FormAlert state={state} />
      {stayError && (
        <p className="text-sm text-destructive" role="alert">
          {stayError}
        </p>
      )}
      <Field name="guestName" label={t("guestName")} required autoComplete="off" defaultValue={v.guestName} error={e.guestName} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field
          name="guestPhone"
          type="tel"
          inputMode="tel"
          label={t("guestPhone")}
          placeholder="+996 555 123 456"
          defaultValue={v.guestPhone}
          error={e.guestPhone}
          hint={t("contactHint")}
        />
        <Field name="guestEmail" type="email" label={t("guestEmail")} defaultValue={v.guestEmail} error={e.guestEmail} />
      </div>
      <div className="grid gap-1.5">
        <Label htmlFor="comment">{t("comment")}</Label>
        <textarea id="comment" name="comment" className={textarea} maxLength={1000} defaultValue={v.comment} aria-describedby="comment-hint" />
        <p id="comment-hint" className="text-xs text-muted-foreground">
          {t("commentHint")}
        </p>
      </div>
      <Field
        name="promoCode"
        label={t("promoCode")}
        autoComplete="off"
        className="uppercase"
        defaultValue={v.promoCode}
        error={e.promoCode}
        hint={t("promoHint")}
      />
      {services.length > 0 && (
        <fieldset className="grid gap-2">
          <legend className="mb-1 text-sm font-medium">{t("services")}</legend>
          {services.map((s) => (
            <label key={s.id} className="flex items-start gap-2 text-sm">
              <Checkbox name="serviceIds" value={s.id} className="mt-0.5" />
              <span>
                {s.name} — <span className="tabular">{formatMoney(s.price)}</span>{" "}
                <span className="text-muted-foreground">{s.isPerNight ? t("perNight") : t("once")}</span>
              </span>
            </label>
          ))}
        </fieldset>
      )}
      <label className="flex items-start gap-2 text-sm">
        <Checkbox name="confirm" value="true" defaultChecked className="mt-0.5" />
        <span className="grid gap-0.5">
          {t("confirmNow")}
          <span className="text-xs text-muted-foreground">{t("confirmHint")}</span>
        </span>
      </label>
      <div>
        <SubmitButton pending={pending}>{t("create")}</SubmitButton>
      </div>
    </form>
  );
}

export function BookingDetailsForm({ booking: b }: { booking: Booking }) {
  const t = useTranslations("Bookings");
  const [state, action, pending] = useActionState<FormState, FormData>(updateBookingAction, {});
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-4" noValidate>
      <input type="hidden" name="id" value={b.id} />
      <input type="hidden" name="version" value={b.version} />
      <FormAlert state={state} success={t("saved")} />
      <Field name="guestName" label={t("guestName")} required defaultValue={v.guestName ?? b.guestName} error={e.guestName} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field name="guestPhone" type="tel" label={t("guestPhone")} defaultValue={v.guestPhone ?? b.guestPhone ?? ""} error={e.guestPhone} />
        <Field name="guestEmail" type="email" label={t("guestEmail")} defaultValue={v.guestEmail ?? b.guestEmail ?? ""} error={e.guestEmail} />
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field name="adults" type="number" min={1} max={20} label={t("adults")} defaultValue={v.adults ?? b.adults} error={e.adults} />
        <Field name="children" type="number" min={0} max={20} label={t("children")} defaultValue={v.children ?? b.children} error={e.children} />
      </div>
      <div className="grid gap-1.5">
        <Label htmlFor="edit-comment">{t("comment")}</Label>
        <textarea id="edit-comment" name="comment" className={textarea} maxLength={1000} defaultValue={v.comment ?? b.comment ?? ""} />
      </div>
      <div>
        <SubmitButton pending={pending} variant="outline">
          {t("save")}
        </SubmitButton>
      </div>
    </form>
  );
}

/** Действия по статусу брони и правам сотрудника: подтверждение — `booking.edit`, отмена — `booking.cancel` (права проверяет и API). */
export function BookingActions({ booking: b, canConfirm, canCancel }: { booking: Booking; canConfirm: boolean; canCancel: boolean }) {
  const t = useTranslations("Bookings");
  const [confirmState, confirm, confirming] = useActionState<FormState, FormData>(confirmBookingAction, {});
  const [cancelState, cancel, cancelling] = useActionState<FormState, FormData>(cancelBookingAction, {});
  const showConfirm = canConfirm && b.allowedActions.includes("confirm");
  const showCancel = canCancel && b.allowedActions.includes("cancel");
  if (!showConfirm && !showCancel) return <p className="text-sm text-muted-foreground">{t("noActions")}</p>;
  return (
    <div className="grid gap-6">
      {showConfirm && (
        <form action={confirm} className="grid gap-3">
          <input type="hidden" name="id" value={b.id} />
          <FormAlert state={confirmState} />
          <div>
            <SubmitButton pending={confirming}>{t("confirm")}</SubmitButton>
          </div>
        </form>
      )}
      {showCancel && <CancelBookingForm booking={b} state={cancelState} action={cancel} pending={cancelling} />}
    </div>
  );
}

/**
 * Отмена брони — с окном подтверждения: действие необратимо, номер освобождается.
 * Без причины окно не открывается — сразу показывается ошибка поля (проверка и в API).
 */
function CancelBookingForm({
  booking: b,
  state,
  action,
  pending,
}: {
  booking: Booking;
  state: FormState;
  action: (form: FormData) => void;
  pending: boolean;
}) {
  const t = useTranslations("Bookings");
  const formRef = useRef<HTMLFormElement>(null);
  const [asking, setAsking] = useState(false);
  return (
    <form ref={formRef} action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="id" value={b.id} />
      <FormAlert state={state} />
      <Field
        name="reason"
        label={t("cancelReason")}
        required
        defaultValue={state.values?.reason}
        error={state.fieldErrors?.reason}
        hint={t("cancelHint")}
      />
      <div>
        <SubmitButton
          pending={pending}
          variant="destructive"
          onClick={(e) => {
            const reason = new FormData(formRef.current!).get("reason");
            if (typeof reason === "string" && reason.trim() !== "") {
              e.preventDefault();
              setAsking(true);
            }
          }}
        >
          {t("cancel")}
        </SubmitButton>
      </div>
      <ConfirmDialog
        open={asking}
        onOpenChange={setAsking}
        title={t("cancelConfirmTitle", { number: b.number })}
        description={t("cancelConfirmText")}
        confirmLabel={t("cancelConfirmYes")}
        cancelLabel={t("cancelConfirmNo")}
        destructive
        onConfirm={() => formRef.current?.requestSubmit()}
      />
    </form>
  );
}

/**
 * Продление: выбор «на сколько» → расчёт API (новое время выезда, доплата, свободен ли номер) →
 * окно подтверждения → продление. Версия брони защищает от двойного продления.
 */
export function ExtendBookingForm({ booking: b, maxUnits }: { booking: Booking; maxUnits: number }) {
  const t = useTranslations("Bookings");
  const [state, action, pending] = useActionState<FormState, FormData>(extendBookingAction, {});
  const [quoteState, setQuoteState] = useState<FormState>({});
  const [quote, setQuote] = useState<ExtensionQuote | null>(null);
  const [quoting, startQuote] = useTransition();
  const formRef = useRef<HTMLFormElement>(null);
  const hourly = b.kind === "Hourly";
  const options = Array.from({ length: Math.max(0, maxUnits) }, (_, i) => i + 1);

  if (options.length === 0) return <p className="text-sm text-muted-foreground">{t("extendNotAvailable")}</p>;

  return (
    <form ref={formRef} action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="id" value={b.id} />
      <input type="hidden" name="version" value={b.version} />
      <FormAlert state={state.error ? state : quoteState.error ? quoteState : state} success={t("extended")} />
      <div className="flex flex-wrap items-end gap-2">
        <SelectField
          name="units"
          id="extend-units"
          label={hourly ? t("extendHours") : t("extendNights")}
          defaultValue="1"
          className="w-32"
          error={state.fieldErrors?.units ?? quoteState.fieldErrors?.units}
        >
          {options.map((n) => (
            <option key={n} value={n}>
              {hourly ? `+${n} ч` : `+${nightsLabel(n)}`}
            </option>
          ))}
        </SelectField>
        <SubmitButton
          pending={pending || quoting}
          variant="outline"
          onClick={(e) => {
            e.preventDefault();
            const units = Number(new FormData(formRef.current!).get("units"));
            startQuote(async () => {
              const result = await quoteExtensionAction(b.id, units);
              if (result.ok) {
                setQuoteState({});
                setQuote(result.quote);
              } else {
                setQuote(null);
                setQuoteState(result.state);
              }
            });
          }}
        >
          {t("extend")}
        </SubmitButton>
      </div>
      <p className="text-xs text-muted-foreground">{t("extendHint")}</p>
      <ConfirmDialog
        open={quote !== null}
        onOpenChange={(open) => !open && setQuote(null)}
        title={t("extendConfirmTitle", { number: b.number })}
        description={
          quote
            ? t("extendConfirmText", { end: formatHotelLocal(quote.newEndsAt), amount: formatMoney(quote.amount) })
            : ""
        }
        confirmLabel={t("extendConfirmYes")}
        cancelLabel={t("extendConfirmNo")}
        onConfirm={() => formRef.current?.requestSubmit()}
      />
    </form>
  );
}

