"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useActionState, useEffect, useRef, useState, useTransition } from "react";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { DateRangePicker } from "@/components/forms/date-range-picker";
import { Field } from "@/components/forms/field";
import { NativeSelect, SelectField } from "@/components/forms/native-select";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import { Checkbox } from "@/components/ui/checkbox";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import { nightsLabel } from "@/lib/bookings/status";
import type { Booking, CalendarRoom, CancellationQuote, ExtensionQuote, ExtraService, GuestLookup, RescheduleQuote } from "@/lib/api/types";
import type { RescheduleInput } from "@/lib/validation/bookings";
import { lookupGuestsAction } from "../guests/actions";
import { formatHotelLocal, formatHotelTime, formatMoney } from "@/lib/format";
import {
  cancelBookingAction,
  confirmBookingAction,
  createBookingAction,
  extendBookingAction,
  noShowBookingAction,
  quoteExtensionAction,
  quoteRescheduleAction,
  rescheduleBookingAction,
  updateBookingAction,
} from "./actions";

const textarea =
  "min-h-20 w-full rounded-lg border border-input bg-card px-2.5 py-2 text-base outline-none md:text-sm focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive";

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
export function CreateBookingForm({ stay, services, initialGuest = null }: { stay: StayParams; services: ExtraService[]; initialGuest?: GuestLookup | null }) {
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
  const [guest, setGuest] = useState<GuestLookup | null>(initialGuest);
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
      <fieldset className="grid gap-3">
        <legend className="mb-1 text-sm font-medium">{t("guestSection")}</legend>
        {guest ? (
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-primary/30 bg-primary/5 px-3 py-2.5">
            <input type="hidden" name="guestId" value={guest.id} />
            <div className="grid text-sm">
              <span className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t("guestChosen")}</span>
              <span className="font-medium">{guest.fullName}</span>
              <span className="tabular text-muted-foreground">
                {[guest.phone, guest.email].filter(Boolean).join(" · ")} · {t("guestBookingsCount", { n: guest.bookingsCount })}
              </span>
            </div>
            <Button type="button" variant="outline" size="sm" onClick={() => setGuest(null)}>
              {t("guestChange")}
            </Button>
          </div>
        ) : (
          <>
            <GuestSearch onChoose={setGuest} />
            <Field name="guestName" label={t("guestName")} required autoComplete="off" defaultValue={v.guestName} error={e.guestName} />
            <div className="grid gap-4 sm:grid-cols-2">
              <Field
                name="guestPhone"
                type="tel"
                inputMode="tel"
                label={t("guestPhone")}
                placeholder="+996 XXX XXX XXX"
                defaultValue={v.guestPhone}
                error={e.guestPhone}
                hint={t("contactHint")}
              />
              <Field name="guestEmail" type="email" label={t("guestEmail")} defaultValue={v.guestEmail} error={e.guestEmail} />
            </div>
            <p className="text-xs text-muted-foreground">{t("guestNewHint")}</p>
          </>
        )}
      </fieldset>
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
export function BookingActions({
  booking: b,
  canConfirm,
  canCancel,
  terms,
}: {
  booking: Booking;
  canConfirm: boolean;
  canCancel: boolean;
  /** Условия отмены и штраф за неявку на сейчас (считает API). */
  terms: CancellationQuote | null;
}) {
  const t = useTranslations("Bookings");
  const [confirmState, confirm, confirming] = useActionState<FormState, FormData>(confirmBookingAction, {});
  const [cancelState, cancel, cancelling] = useActionState<FormState, FormData>(cancelBookingAction, {});
  const showConfirm = canConfirm && b.allowedActions.includes("confirm");
  const showCancel = canCancel && b.allowedActions.includes("cancel");
  const showNoShow = canConfirm && b.allowedActions.includes("noShow");
  if (!showConfirm && !showCancel && !showNoShow) return <p className="text-sm text-muted-foreground">{t("noActions")}</p>;
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
      {showNoShow && <NoShowForm booking={b} terms={terms} />}
      {showCancel && <CancelBookingForm booking={b} terms={terms} state={cancelState} action={cancel} pending={cancelling} />}
    </div>
  );
}

/** Сколько и за что штраф — «1 ч», «1 ночь». */
function unitsLabel(unit: CancellationQuote["unit"], n: number) {
  return unit === "Hour" ? `${n} ч` : nightsLabel(n);
}

/**
 * Неявка: гость не пришёл к началу. Номер освобождается, начисляется штраф по правилам.
 * Снять штраф может только сотрудник с правом «Отмена без штрафа» (проверяет и API).
 */
function NoShowForm({ booking: b, terms }: { booking: Booking; terms: CancellationQuote | null }) {
  const t = useTranslations("Bookings");
  const [state, action, pending] = useActionState<FormState, FormData>(noShowBookingAction, {});
  const formRef = useRef<HTMLFormElement>(null);
  const [asking, setAsking] = useState(false);
  const penalty = terms?.noShowPenalty ?? 0;
  return (
    <form ref={formRef} action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="id" value={b.id} />
      <input type="hidden" name="version" value={b.version} />
      <h3 className="font-heading text-base">{t("noShowTitle")}</h3>
      <FormAlert state={state} />
      <p className="text-sm text-muted-foreground">
        {penalty > 0 ? t("noShowText", { amount: formatMoney(penalty) }) : t("noShowTextFree")}
      </p>
      {terms?.canWaive && penalty > 0 && (
        <label className="flex items-start gap-2 text-sm">
          <Checkbox name="waivePenalty" value="true" className="mt-0.5" />
          <span>{t("waivePenalty")}</span>
        </label>
      )}
      <div>
        <SubmitButton
          pending={pending}
          variant="outline"
          onClick={(e) => {
            e.preventDefault();
            setAsking(true);
          }}
        >
          {t("noShow")}
        </SubmitButton>
      </div>
      <ConfirmDialog
        open={asking}
        onOpenChange={setAsking}
        title={t("noShowConfirmTitle", { number: b.number })}
        description={t("noShowConfirmText")}
        confirmLabel={t("noShowConfirmYes")}
        cancelLabel={t("cancelConfirmNo")}
        destructive
        onConfirm={() => formRef.current?.requestSubmit()}
      />
    </form>
  );
}

/**
 * Отмена брони — с окном подтверждения: действие необратимо, номер освобождается.
 * Без причины окно не открывается — сразу показывается ошибка поля (проверка и в API).
 */
function CancelBookingForm({
  booking: b,
  terms,
  state,
  action,
  pending,
}: {
  booking: Booking;
  terms: CancellationQuote | null;
  state: FormState;
  action: (form: FormData) => void;
  pending: boolean;
}) {
  const t = useTranslations("Bookings");
  const formRef = useRef<HTMLFormElement>(null);
  const [asking, setAsking] = useState(false);
  const late = terms !== null && !terms.isFree;
  const termsText = !terms
    ? null
    : !terms.isFree
      ? t("cancelLate", { time: terms.freeUntil ? formatHotelLocal(terms.freeUntil) : "—", amount: formatMoney(terms.penalty), units: unitsLabel(terms.unit, terms.units) })
      : terms.freeUntil
        ? t("cancelFreeUntil", { time: formatHotelLocal(terms.freeUntil) })
        : b.status === "Confirmed"
          ? t("cancelFreeNoPenalties")
          : t("cancelFreeUnconfirmed");
  return (
    <form ref={formRef} action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="id" value={b.id} />
      <h3 className="font-heading text-base">{t("cancelTitle")}</h3>
      <FormAlert state={state} />
      {termsText && (
        <p className={late ? "rounded-xl bg-destructive/8 px-3 py-2 text-sm text-destructive" : "text-sm text-muted-foreground"}>{termsText}</p>
      )}
      {late && terms?.canWaive && (
        <label className="flex items-start gap-2 text-sm">
          <Checkbox name="waivePenalty" value="true" className="mt-0.5" />
          <span>{t("waivePenalty")}</span>
        </label>
      )}
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
        description={late ? t("cancelConfirmTextPenalty", { amount: formatMoney(terms!.penalty) }) : t("cancelConfirmText")}
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


/**
 * Поиск гостя по базе при брони: имя, телефон или email. Запрос — через серверное действие (права проверяет API),
 * с задержкой, чтобы не спрашивать сервер на каждую букву.
 */
function GuestSearch({ onChoose }: { onChoose: (guest: GuestLookup) => void }) {
  const t = useTranslations("Bookings");
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<GuestLookup[] | null>(null);
  const [searching, startSearch] = useTransition();
  const timer = useRef<number | undefined>(undefined);

  function onChange(value: string) {
    setQuery(value);
    window.clearTimeout(timer.current);
    if (value.trim().length < 2) {
      setResults(null);
      return;
    }
    timer.current = window.setTimeout(() => startSearch(async () => setResults(await lookupGuestsAction(value))), 300);
  }

  return (
    <div className="grid gap-1.5">
      <Label htmlFor="guest-search">{t("guestSearch")}</Label>
      <Input
        id="guest-search"
        type="search"
        value={query}
        onChange={(ev) => onChange(ev.target.value)}
        onKeyDown={(ev) => ev.key === "Enter" && ev.preventDefault()}
        placeholder={t("guestSearchPlaceholder")}
        autoComplete="off"
        aria-controls="guest-results"
      />
      <div id="guest-results" aria-live="polite">
        {searching && <p className="text-xs text-muted-foreground">{t("guestSearching")}</p>}
        {!searching && results && results.length === 0 && <p className="text-xs text-muted-foreground">{t("guestNoMatches")}</p>}
        {!searching && results && results.length > 0 && (
          <ul className="grid gap-1 rounded-xl border bg-card p-1">
            {results.map((g) => (
              <li key={g.id}>
                <button
                  type="button"
                  onClick={() => onChoose(g)}
                  className="grid w-full rounded-lg px-3 py-2 text-left text-sm transition hover:bg-accent focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
                >
                  <span className="font-medium">{g.fullName}</span>
                  <span className="tabular text-xs text-muted-foreground">
                    {[g.phone, g.email].filter(Boolean).join(" · ")} · {t("guestBookingsCount", { n: g.bookingsCount })}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

/**
 * Перенос брони без календаря (доступно с клавиатуры): новое время и номер → расчёт API → окно подтверждения.
 * Вид брони (часы/сутки) не меняется. Цена — по текущим ценам, разницу показываем до переноса.
 */
export function RescheduleForm({
  booking: b,
  rooms,
  today,
  minHours,
  maxHours,
}: {
  booking: Booking;
  rooms: CalendarRoom[];
  today: string;
  minHours: number;
  maxHours: number;
}) {
  const t = useTranslations("Bookings");
  const router = useRouter();
  const formRef = useRef<HTMLFormElement>(null);
  const [state, setState] = useState<FormState>({});
  const [plan, setPlan] = useState<{ input: RescheduleInput; quote: RescheduleQuote } | null>(null);
  const [busy, startBusy] = useTransition();
  const hourly = b.kind === "Hourly";
  const suitable = rooms.filter((r) => r.isSellable && (!hourly || r.sellsHourly) && r.capacity >= b.adults + b.children);
  const startHour = Number(b.startsAt.slice(11, 13));
  const hoursOptions = Array.from({ length: Math.max(0, maxHours - minHours + 1) }, (_, i) => minHours + i);

  function read(): RescheduleInput {
    const f = new FormData(formRef.current!);
    const roomId = String(f.get("roomId") ?? "");
    return hourly
      ? { kind: "Hourly", roomId, date: String(f.get("date") ?? ""), startHour: Number(f.get("startHour")), hours: Number(f.get("hours")) }
      : { kind: "Nightly", roomId, checkIn: String(f.get("checkIn") ?? ""), checkOut: String(f.get("checkOut") ?? "") };
  }

  const e = state.fieldErrors ?? {};
  return (
    <form ref={formRef} className="grid gap-3" noValidate onSubmit={(ev) => ev.preventDefault()}>
      <FormAlert state={state} success={t("rescheduled")} />
      {hourly ? (
        <div className="grid gap-3 sm:grid-cols-2">
          <DateRangePicker mode="single" size="compact" startName="date" defaultStart={b.startsAt.slice(0, 10)} today={today} min={today} startLabel={t("date")} />
          <div className="grid grid-cols-2 gap-2">
            <div className="grid gap-1.5">
              <Label htmlFor="rs-start">{t("startTime")}</Label>
              <NativeSelect id="rs-start" name="startHour" defaultValue={String(startHour)}>
                {Array.from({ length: 24 }, (_, h) => (
                  <option key={h} value={h}>
                    {String(h).padStart(2, "0")}:00
                  </option>
                ))}
              </NativeSelect>
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="rs-hours">{t("hoursLabel")}</Label>
              <NativeSelect id="rs-hours" name="hours" defaultValue={String(b.hours ?? minHours)}>
                {hoursOptions.map((h) => (
                  <option key={h} value={h}>
                    {h} ч
                  </option>
                ))}
              </NativeSelect>
            </div>
          </div>
        </div>
      ) : (
        <DateRangePicker
          mode="stay"
          size="compact"
          startName="checkIn"
          endName="checkOut"
          defaultStart={b.checkIn}
          defaultEnd={b.checkOut}
          today={today}
          min={today}
          startLabel={t("checkIn")}
          endLabel={t("checkOut")}
        />
      )}
      {(e.date || e.checkIn || e.checkOut || e.startHour || e.hours) && (
        <p className="text-sm text-destructive" role="alert">
          {e.date ?? e.checkIn ?? e.checkOut ?? e.startHour ?? e.hours}
        </p>
      )}
      <SelectField name="roomId" id="rs-room" label={t("room")} defaultValue={b.room.roomId} error={e.roomId}>
        {suitable.map((r) => (
          <option key={r.id} value={r.id}>
            № {r.number} · {r.roomTypeName}
          </option>
        ))}
      </SelectField>
      <div>
        <SubmitButton
          pending={busy}
          variant="outline"
          onClick={(ev) => {
            ev.preventDefault();
            const input = read();
            startBusy(async () => {
              const result = await quoteRescheduleAction(b.id, input);
              if (result.ok) {
                setState({});
                setPlan({ input, quote: result.data });
              } else {
                setPlan(null);
                setState(result.state);
              }
            });
          }}
        >
          {t("reschedule")}
        </SubmitButton>
      </div>
      <p className="text-xs text-muted-foreground">{t("rescheduleHint")}</p>
      <ConfirmDialog
        open={plan !== null}
        onOpenChange={(open) => !open && setPlan(null)}
        title={t("rescheduleConfirmTitle", { number: b.number })}
        description={
          plan
            ? t("rescheduleConfirmText", {
                when: hourly
                  ? `${formatHotelLocal(plan.quote.startsAt)}–${formatHotelTime(plan.quote.endsAt)}`
                  : `${formatHotelLocal(plan.quote.startsAt)} — ${formatHotelLocal(plan.quote.endsAt)}`,
                room: plan.quote.roomNumber,
                from: formatMoney(plan.quote.oldTotal),
                to: formatMoney(plan.quote.newTotal),
              })
            : ""
        }
        confirmLabel={t("rescheduleYes")}
        cancelLabel={t("extendConfirmNo")}
        onConfirm={() => {
          if (!plan) return;
          const { input } = plan;
          setPlan(null);
          startBusy(async () => {
            const result = await rescheduleBookingAction(b.id, input, b.version);
            if (result.ok) {
              setState({ ok: true });
              router.refresh();
            } else {
              setState(result.state);
            }
          });
        }}
      />
    </form>
  );
}
