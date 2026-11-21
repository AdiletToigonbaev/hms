"use client";

import { DoorClosed, DoorOpen } from "lucide-react";
import { useTranslations } from "next-intl";
import { useActionState, useEffect, useRef, useState } from "react";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import { Checkbox } from "@/components/ui/checkbox";
import type { FormState } from "@/lib/api/problem";
import type { Booking, CheckInPreview, CheckOutPreview } from "@/lib/api/types";
import { nightsLabel } from "@/lib/bookings/status";
import { formatHotelLocal, formatHotelTime, formatMoney } from "@/lib/format";
import { checkInAction, checkOutAction } from "../actions";

function newKey() {
  return crypto.randomUUID().replace(/-/g, "");
}

/** Ключ идемпотентности: новый после отказа API, чтобы повторная попытка не вернула сохранённую ошибку. */
function useIdempotencyKey(state: FormState) {
  const [key, setKey] = useState(newKey);
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- ключ меняется только после ответа API
    if (state.ok === false) setKey(newKey());
  }, [state]);
  return key;
}

/** Заселение: можно ли сейчас (окно, готовность номера) и сколько гость ещё должен. Всё считает API. */
export function CheckInForm({ booking: b, preview }: { booking: Booking; preview: CheckInPreview }) {
  const t = useTranslations("FrontDesk");
  const [state, action, pending] = useActionState<FormState, FormData>(checkInAction, {});
  const key = useIdempotencyKey(state);
  return (
    <form action={action} className="grid gap-3">
      <input type="hidden" name="id" value={b.id} />
      <input type="hidden" name="idempotencyKey" value={key} />
      <h3 className="flex items-center gap-2 font-heading text-base">
        <DoorOpen className="size-4 text-primary" aria-hidden />
        {t("checkInTitle")}
      </h3>
      <FormAlert state={state} />
      {preview.allowed ? (
        <>
          <p className="text-sm text-muted-foreground">
            {t("checkInText", { room: preview.roomNumber })}
            {preview.early && ` ${t("checkInEarly", { time: formatHotelTime(preview.startsAt) })}`}
          </p>
          {preview.balance > 0 && (
            <p className="text-sm">
              {t("toPay")}: <span className="tabular font-medium">{formatMoney(preview.balance)}</span>
            </p>
          )}
          <div>
            <SubmitButton pending={pending}>{t("checkIn")}</SubmitButton>
          </div>
        </>
      ) : (
        <p role="status" className="text-sm text-muted-foreground">
          {preview.blockMessage}
        </p>
      )}
    </form>
  );
}

/**
 * Выселение: досрочный выезд (перерасчёт по правилам гостиницы), опоздание, долг.
 * С долгом — только с явной отметкой; подтверждение в окне: номер уйдёт на уборку.
 */
export function CheckOutForm({ booking: b, preview }: { booking: Booking; preview: CheckOutPreview }) {
  const t = useTranslations("FrontDesk");
  const [state, action, pending] = useActionState<FormState, FormData>(checkOutAction, {});
  const key = useIdempotencyKey(state);
  const formRef = useRef<HTMLFormElement>(null);
  const [asking, setAsking] = useState(false);
  const [allowDebt, setAllowDebt] = useState(false);
  const debt = preview.balanceAfter > 0;
  const units = (n: number) => (preview.unit === "Hour" ? `${n} ч` : nightsLabel(n));
  return (
    <form ref={formRef} action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="id" value={b.id} />
      <input type="hidden" name="idempotencyKey" value={key} />
      <h3 className="flex items-center gap-2 font-heading text-base">
        <DoorClosed className="size-4 text-primary" aria-hidden />
        {t("checkOutTitle")}
      </h3>
      <FormAlert state={state} />
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted-foreground">{t("plannedEnd")}</dt>
        <dd className="tabular">{formatHotelLocal(preview.plannedEndsAt)}</dd>
        {preview.early && (
          <>
            <dt className="text-muted-foreground">{t("earlyDeparture")}</dt>
            <dd>
              {preview.unusedUnits > 0 ? t("unused", { n: units(preview.unusedUnits) }) : t("unusedNone")}
              {preview.credit > 0 ? ` · ${t("credit", { amount: formatMoney(preview.credit) })}` : preview.unusedUnits > 0 ? ` · ${t("noCredit")}` : ""}
            </dd>
          </>
        )}
        {preview.lateMinutes > 0 && (
          <>
            <dt className="text-muted-foreground">{t("late")}</dt>
            <dd className="text-status-cleaning">{t("lateMinutes", { n: preview.lateMinutes })}</dd>
          </>
        )}
        <dt className="text-muted-foreground">{t("total")}</dt>
        <dd className="tabular">
          {preview.credit > 0 && <s className="mr-2 text-muted-foreground">{formatMoney(preview.totalBefore)}</s>}
          {formatMoney(preview.totalAfter)}
        </dd>
        <dt className="text-muted-foreground">{t("received")}</dt>
        <dd className="tabular">{formatMoney(preview.received)}</dd>
        {preview.balanceAfter !== 0 && (
          <>
            <dt className="text-muted-foreground">{preview.balanceAfter > 0 ? t("debt") : t("overpaid")}</dt>
            <dd className={`tabular font-medium ${preview.balanceAfter > 0 ? "text-destructive" : ""}`}>{formatMoney(Math.abs(preview.balanceAfter))}</dd>
          </>
        )}
        <dt className="text-muted-foreground">{t("cleaningDue")}</dt>
        <dd className="tabular">{formatHotelTime(preview.cleaningDueAt)}</dd>
      </dl>
      {preview.balanceAfter < 0 && <p className="text-xs text-muted-foreground">{t("overpaidHint")}</p>}
      {debt && (
        <label className="flex items-start gap-2 text-sm">
          <Checkbox name="allowDebt" value="true" className="mt-0.5" checked={allowDebt} onCheckedChange={(v) => setAllowDebt(v === true)} />
          <span>{t("allowDebt", { amount: formatMoney(preview.balanceAfter) })}</span>
        </label>
      )}
      <div>
        <SubmitButton
          pending={pending}
          disabled={pending || (debt && !allowDebt)}
          onClick={(e) => {
            e.preventDefault();
            setAsking(true);
          }}
        >
          {t("checkOut")}
        </SubmitButton>
      </div>
      <ConfirmDialog
        open={asking}
        onOpenChange={setAsking}
        title={t("checkOutConfirmTitle", { number: b.number })}
        description={preview.early ? t("checkOutConfirmEarly") : t("checkOutConfirmText")}
        confirmLabel={t("checkOutConfirmYes")}
        cancelLabel={t("no")}
        onConfirm={() => formRef.current?.requestSubmit()}
      />
    </form>
  );
}
