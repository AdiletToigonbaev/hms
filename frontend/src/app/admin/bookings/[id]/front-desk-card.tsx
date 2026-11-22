"use client";

import { ArrowRightLeft, DoorClosed, DoorOpen } from "lucide-react";
import { useTranslations } from "next-intl";
import { useActionState, useEffect, useRef, useState } from "react";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import { NativeSelect } from "@/components/forms/native-select";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import type { Booking, CheckInPreview, CheckOutPreview, MoveOptions } from "@/lib/api/types";
import { nightsLabel } from "@/lib/bookings/status";
import { formatHotelLocal, formatHotelTime, formatMoney } from "@/lib/format";
import { checkInAction, checkOutAction, moveGuestAction } from "../actions";

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

const textarea =
  "min-h-16 w-full rounded-lg border border-input bg-card px-2.5 py-2 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 md:text-sm";

/**
 * Переселение заселённого гостя. Номера — только готовые и свободные до выезда (считает API).
 * Цену выбирает администратор: без изменения (например, по жалобе) или пересчёт оставшегося времени по цене нового номера.
 */
export function MoveGuestForm({ booking: b, options }: { booking: Booking; options: MoveOptions }) {
  const t = useTranslations("FrontDesk");
  const [state, action, pending] = useActionState<FormState, FormData>(moveGuestAction, {});
  const key = useIdempotencyKey(state);
  const formRef = useRef<HTMLFormElement>(null);
  const [asking, setAsking] = useState(false);
  const [roomId, setRoomId] = useState(options.rooms[0]?.roomId ?? "");
  const [price, setPrice] = useState<"keep" | "recalculate">("keep");
  const room = options.rooms.find((r) => r.roomId === roomId);
  const units = options.unit === "Hour" ? `${options.remainingUnits.toLocaleString("ru-RU")} ч` : nightsLabel(options.remainingUnits);
  const diff = room?.difference ?? 0;
  const diffText = diff === 0 ? t("moveSamePrice") : diff > 0 ? t("moveMore", { amount: formatMoney(diff) }) : t("moveLess", { amount: formatMoney(-diff) });
  const newTotal = price === "recalculate" ? Math.max(0, options.total + diff) : options.total;
  return (
    <form ref={formRef} action={action} className="grid gap-3 border-t pt-4" noValidate>
      <input type="hidden" name="id" value={b.id} />
      <input type="hidden" name="version" value={b.version} />
      <input type="hidden" name="idempotencyKey" value={key} />
      <h3 className="flex items-center gap-2 font-heading text-base">
        <ArrowRightLeft className="size-4 text-primary" aria-hidden />
        {t("moveTitle")}
      </h3>
      <FormAlert state={state} success={t("moved")} />
      {options.rooms.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("moveNoRooms")}</p>
      ) : (
        <>
          <div className="grid gap-1.5">
            <Label htmlFor="move-room">{t("moveTo", { from: options.currentRoomNumber })}</Label>
            <NativeSelect id="move-room" name="roomId" value={roomId} onChange={(e) => setRoomId(e.target.value)}>
              {options.rooms.map((r) => (
                <option key={r.roomId} value={r.roomId}>
                  № {r.number} · {r.roomTypeName}
                </option>
              ))}
            </NativeSelect>
            <p className="text-xs text-muted-foreground">{t("moveRoomsHint")}</p>
          </div>
          <fieldset className="grid gap-2">
            <legend className="mb-1 text-sm font-medium">{t("movePrice")}</legend>
            <label className="flex items-start gap-2 text-sm">
              <input type="radio" name="price" value="keep" checked={price === "keep"} onChange={() => setPrice("keep")} className="mt-1 accent-primary" />
              <span>{t("moveKeep")}</span>
            </label>
            <label className="flex items-start gap-2 text-sm">
              <input
                type="radio"
                name="price"
                value="recalculate"
                checked={price === "recalculate"}
                onChange={() => setPrice("recalculate")}
                className="mt-1 accent-primary"
              />
              <span>
                {t("moveRecalculate", { units })} — <span className="tabular font-medium">{diffText}</span>
              </span>
            </label>
          </fieldset>
          <div className="grid gap-1.5">
            <Label htmlFor="move-reason">{t("moveReason")}</Label>
            <textarea id="move-reason" name="reason" maxLength={500} placeholder={t("moveReasonPlaceholder")} className={textarea} />
          </div>
          <div>
            <SubmitButton
              pending={pending}
              variant="outline"
              disabled={pending || !roomId}
              onClick={(e) => {
                e.preventDefault();
                setAsking(true);
              }}
            >
              {t("move")}
            </SubmitButton>
          </div>
          <ConfirmDialog
            open={asking}
            onOpenChange={setAsking}
            title={t("moveConfirmTitle", { from: options.currentRoomNumber, to: room?.number ?? "" })}
            description={t("moveConfirmText", { total: formatMoney(newTotal) })}
            confirmLabel={t("moveConfirmYes")}
            cancelLabel={t("no")}
            onConfirm={() => formRef.current?.requestSubmit()}
          />
        </>
      )}
    </form>
  );
}
