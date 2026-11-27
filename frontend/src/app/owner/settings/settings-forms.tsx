"use client";

import { Send } from "lucide-react";
import { useTranslations } from "next-intl";
import { useActionState, useState, type ReactNode } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { HelpTip } from "@/components/forms/help-tip";
import { SelectField } from "@/components/forms/native-select";
import { SubmitButton } from "@/components/forms/submit-button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import type { Hotel, HotelSettings, Integration } from "@/lib/api/types";
import {
  describeRules,
  rulesFromSettings,
  type BookingRulesValues,
} from "@/lib/settings/rules";
import { cn } from "@/lib/utils";
import type messages from "../../../../messages/ru.json";
import {
  saveHotelAction,
  saveIntegrationAction,
  saveSettingsSectionAction,
  testIntegrationAction,
} from "./actions";

const textarea =
  "min-h-24 w-full rounded-lg border border-input bg-card px-2.5 py-2 text-base outline-none md:text-sm focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50";

type HelpKey = keyof (typeof messages)["SettingsHelp"];

/** Подсказка «?» к настройке: что делает, пример, когда заработает (если ещё не применяется). */
function SettingHelp({ helpKey, title }: { helpKey: HelpKey; title: string }) {
  const h = useTranslations("SettingsHelp");
  const key = (suffix: "text" | "example" | "pending") =>
    `${helpKey}.${suffix}` as Parameters<typeof h>[0];
  return (
    <HelpTip
      title={title}
      text={h(key("text"))}
      example={h.has(key("example")) ? h(key("example")) : undefined}
      pending={h.has(key("pending")) ? h(key("pending")) : undefined}
    />
  );
}

function help(helpKey: HelpKey, title: string) {
  return <SettingHelp helpKey={helpKey} title={title} />;
}

function Toggle({
  name,
  label,
  defaultChecked,
  help: tip,
}: {
  name: string;
  label: string;
  defaultChecked: boolean;
  help?: ReactNode;
}) {
  return (
    <div className="flex min-h-11 items-start gap-2 rounded-xl border bg-card px-3 py-2.5 text-sm">
      <label className="flex flex-1 items-start gap-3">
        <Checkbox
          name={name}
          value="true"
          defaultChecked={defaultChecked}
          className="mt-0.5"
        />
        {label}
      </label>
      {tip}
    </div>
  );
}

/** Подвал раздела: своя кнопка «Сохранить» у каждого раздела. */
function SectionFooter({ pending }: { pending: boolean }) {
  const t = useTranslations("Settings");
  return (
    <div className="flex justify-end border-t pt-4">
      <SubmitButton pending={pending}>{t("save")}</SubmitButton>
    </div>
  );
}

// ---------- Гостиница ----------

export function HotelForm({
  hotel,
  rules,
}: {
  hotel: Hotel;
  rules: string | null;
}) {
  const t = useTranslations("Settings");
  const [state, action, pending] = useActionState<FormState, FormData>(
    saveHotelAction,
    {},
  );
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-4" noValidate>
      <input type="hidden" name="version" value={hotel.version} />
      <FormAlert state={state} success={t("savedSection")} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field
          name="name"
          label={t("name")}
          help={help("name", t("name"))}
          hint={t("nameHint")}
          required
          defaultValue={v.name ?? hotel.name}
          error={e.name}
        />
        <Field
          name="address"
          label={t("address")}
          help={help("address", t("address"))}
          defaultValue={v.address ?? hotel.address ?? ""}
          error={e.address}
        />
        <Field
          name="phone"
          type="tel"
          label={t("phone")}
          help={help("phone", t("phone"))}
          defaultValue={v.phone ?? hotel.phone ?? ""}
          error={e.phone}
        />
        <Field
          name="email"
          type="email"
          label={t("email")}
          help={help("email", t("email"))}
          defaultValue={v.email ?? hotel.email ?? ""}
          error={e.email}
        />
      </div>
      <div className="grid gap-1.5">
        <div className="flex items-center gap-1">
          <Label htmlFor="description">{t("aboutHotel")}</Label>
          {help("aboutHotel", t("aboutHotel"))}
        </div>
        <textarea
          id="description"
          name="description"
          className={textarea}
          defaultValue={v.description ?? hotel.description ?? ""}
          maxLength={2000}
        />
      </div>
      <div className="grid gap-1.5">
        <div className="flex items-center gap-1">
          <Label htmlFor="rules">{t("houseRules")}</Label>
          {help("houseRules", t("houseRules"))}
        </div>
        <textarea
          id="rules"
          name="rules"
          className={textarea}
          defaultValue={v.rules ?? rules ?? ""}
          maxLength={5000}
        />
      </div>
      <SectionFooter pending={pending} />
    </form>
  );
}

// ---------- Правила броней: таблица «по часам / по суткам» ----------

const numberInput = "h-9 w-16 tabular text-right";

/** Число с единицей измерения в ячейке таблицы правил. */
function NumberCell({
  name,
  label,
  value,
  min,
  max,
  step,
  unit,
  error,
}: {
  name: string;
  label: string;
  value: string | number;
  min: number;
  max: number;
  step?: number;
  unit: string;
  error?: string;
}) {
  return (
    <div className="grid gap-1">
      <div className="flex items-center gap-2">
        <Input
          id={name}
          name={name}
          type="number"
          inputMode="numeric"
          min={min}
          max={max}
          step={step}
          defaultValue={value}
          aria-label={label}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? `${name}-error` : undefined}
          className={numberInput}
        />
        <span className="text-sm text-muted-foreground">{unit}</span>
      </div>
      {error && (
        <p id={`${name}-error`} className="text-xs text-destructive">
          {error}
        </p>
      )}
    </div>
  );
}

function TimeCell({
  name,
  label,
  value,
  prefix,
  suffix,
  error,
}: {
  name: string;
  label: string;
  value: string;
  prefix?: string;
  suffix?: string;
  error?: string;
}) {
  return (
    <div className="grid gap-1">
      <div className="flex flex-wrap items-center gap-2">
        {prefix && (
          <span className="text-sm text-muted-foreground">{prefix}</span>
        )}
        <Input
          id={name}
          name={name}
          type="time"
          defaultValue={value}
          aria-label={label}
          aria-invalid={error ? true : undefined}
          className="h-9 w-32 tabular"
        />
        {suffix && (
          <span className="text-sm text-muted-foreground">{suffix}</span>
        )}
      </div>
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}

/** Строка таблицы: название с подсказкой, ячейка «по часам», ячейка «по суткам». На телефоне ячейки идут друг под другом с подписью. */
function RuleRow({
  title,
  tip,
  hourly,
  nightly,
}: {
  title: string;
  tip: ReactNode;
  hourly: ReactNode;
  nightly: ReactNode;
}) {
  const t = useTranslations("Settings");
  return (
    <div
      role="row"
      className="-mx-2 grid gap-3 rounded-lg border-t px-2 py-3 transition-colors focus-within:bg-primary/[0.06] hover:bg-secondary/40 md:grid-cols-[minmax(0,1.1fr)_minmax(0,1fr)_minmax(0,1fr)] md:items-start md:gap-4"
    >
      <div
        role="rowheader"
        className="flex items-center gap-1 pt-1.5 text-sm font-medium"
      >
        {title}
        {tip}
      </div>
      <div role="cell" className="grid gap-1">
        <span className="text-xs font-medium tracking-wide text-muted-foreground uppercase md:hidden">
          {t("colHourly")}
        </span>
        {hourly}
      </div>
      <div role="cell" className="grid gap-1">
        <span className="text-xs font-medium tracking-wide text-muted-foreground uppercase md:hidden">
          {t("colNightly")}
        </span>
        {nightly}
      </div>
    </div>
  );
}

function readRules(
  form: HTMLFormElement,
  previous: BookingRulesValues,
): BookingRulesValues {
  const data = new FormData(form);
  const num = (name: keyof BookingRulesValues) => {
    const value = Number(data.get(name));
    return Number.isFinite(value) && data.get(name) !== ""
      ? value
      : (previous[name] as number);
  };
  const str = (name: keyof BookingRulesValues) =>
    String(data.get(name) || previous[name]);
  return {
    hourlyMinHours: num("hourlyMinHours"),
    hourlyMaxHours: num("hourlyMaxHours"),
    hourlyFreeCancelMinutes: num("hourlyFreeCancelMinutes"),
    hourlyLateCancelPenaltyHours: num("hourlyLateCancelPenaltyHours"),
    hourlyNoShowAfterMinutes: num("hourlyNoShowAfterMinutes"),
    hourlyNoShowPenaltyHours: num("hourlyNoShowPenaltyHours"),
    hourlyPendingHoldMinutes: num("hourlyPendingHoldMinutes"),
    hourlyRefundUnused: data.get("hourlyRefundUnused") !== null,
    checkInTime: str("checkInTime"),
    checkOutTime: str("checkOutTime"),
    noShowCutoffTime: str("noShowCutoffTime"),
    freeCancelHoursBeforeCheckIn: num("freeCancelHoursBeforeCheckIn"),
    lateCancelPenaltyNights: num("lateCancelPenaltyNights"),
    noShowPenaltyNights: num("noShowPenaltyNights"),
    pendingHoldHours: num("pendingHoldHours"),
    earlyDeparturePenaltyNights: num("earlyDeparturePenaltyNights"),
  };
}

export function BookingRulesForm({ settings }: { settings: HotelSettings }) {
  const t = useTranslations("Settings");
  const summary = useTranslations("SettingsSummary");
  const [state, action, pending] = useActionState<FormState, FormData>(
    saveSettingsSectionAction,
    {},
  );
  const initial = rulesFromSettings(settings);
  const [values, setValues] = useState<BookingRulesValues>(initial);
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  const val = (name: keyof BookingRulesValues) =>
    v[name] ?? String(initial[name]);
  const text = describeRules(
    values,
    summary as unknown as Parameters<typeof describeRules>[1],
  );

  return (
    <form
      action={action}
      className="grid gap-4"
      noValidate
      onInput={(ev) => setValues(readRules(ev.currentTarget, values))}
    >
      <input type="hidden" name="section" value="rules" />
      <input type="hidden" name="version" value={settings.version} />
      <FormAlert state={state} success={t("savedSection")} />

      <div role="table" aria-label={t("secRules")} className="grid">
        <div
          role="row"
          className="hidden gap-4 pb-2 text-xs font-medium tracking-wide text-muted-foreground uppercase md:grid md:grid-cols-[minmax(0,1.1fr)_minmax(0,1fr)_minmax(0,1fr)]"
        >
          <span role="columnheader">{t("colRule")}</span>
          <span role="columnheader">{t("colHourly")}</span>
          <span role="columnheader">{t("colNightly")}</span>
        </div>

        <RuleRow
          title={t("rowDuration")}
          tip={help("hourlyMin", t("rowDuration"))}
          hourly={
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-sm text-muted-foreground">{t("from")}</span>
              <NumberCell
                name="hourlyMinHours"
                label={`${t("hourlyMin")}`}
                value={val("hourlyMinHours")}
                min={1}
                max={24}
                unit=""
                error={e.hourlyMinHours}
              />
              <span className="text-sm text-muted-foreground">{t("to")}</span>
              <NumberCell
                name="hourlyMaxHours"
                label={`${t("hourlyMax")}`}
                value={val("hourlyMaxHours")}
                min={1}
                max={24}
                unit={t("hoursUnit")}
                error={e.hourlyMaxHours}
              />
            </div>
          }
          nightly={
            <span className="pt-1.5 text-sm text-muted-foreground">—</span>
          }
        />
        <RuleRow
          title={t("rowTime")}
          tip={help("checkIn", t("rowTime"))}
          hourly={
            <span className="pt-1.5 text-sm text-muted-foreground">
              {t("startInBooking")}
            </span>
          }
          nightly={
            <div className="grid gap-2">
              <TimeCell
                name="checkInTime"
                label={t("checkIn")}
                value={val("checkInTime")}
                prefix={t("checkInShort")}
                error={e.checkInTime}
              />
              <TimeCell
                name="checkOutTime"
                label={t("checkOut")}
                value={val("checkOutTime")}
                prefix={t("checkOutShort")}
                error={e.checkOutTime}
              />
            </div>
          }
        />
        <RuleRow
          title={t("rowFreeCancel")}
          tip={help("hourlyFreeCancel", t("rowFreeCancel"))}
          hourly={
            <NumberCell
              name="hourlyFreeCancelMinutes"
              label={`${t("rowFreeCancel")} — ${t("colHourly")}`}
              value={val("hourlyFreeCancelMinutes")}
              min={0}
              max={10080}
              step={15}
              unit={t("beforeStart")}
              error={e.hourlyFreeCancelMinutes}
            />
          }
          nightly={
            <NumberCell
              name="freeCancelHoursBeforeCheckIn"
              label={`${t("rowFreeCancel")} — ${t("colNightly")}`}
              value={val("freeCancelHoursBeforeCheckIn")}
              min={0}
              max={720}
              unit={t("beforeCheckIn")}
              error={e.freeCancelHoursBeforeCheckIn}
            />
          }
        />
        <RuleRow
          title={t("rowLateCancel")}
          tip={help("hourlyLateCancel", t("rowLateCancel"))}
          hourly={
            <NumberCell
              name="hourlyLateCancelPenaltyHours"
              label={`${t("rowLateCancel")} — ${t("colHourly")}`}
              value={val("hourlyLateCancelPenaltyHours")}
              min={0}
              max={24}
              unit={t("hoursUnit")}
              error={e.hourlyLateCancelPenaltyHours}
            />
          }
          nightly={
            <NumberCell
              name="lateCancelPenaltyNights"
              label={`${t("rowLateCancel")} — ${t("colNightly")}`}
              value={val("lateCancelPenaltyNights")}
              min={0}
              max={30}
              unit={t("nightsUnit")}
              error={e.lateCancelPenaltyNights}
            />
          }
        />
        <RuleRow
          title={t("rowNoShow")}
          tip={help("hourlyNoShowAfter", t("rowNoShow"))}
          hourly={
            <NumberCell
              name="hourlyNoShowAfterMinutes"
              label={`${t("rowNoShow")} — ${t("colHourly")}`}
              value={val("hourlyNoShowAfterMinutes")}
              min={5}
              max={720}
              step={5}
              unit={t("afterStart")}
              error={e.hourlyNoShowAfterMinutes}
            />
          }
          nightly={
            <TimeCell
              name="noShowCutoffTime"
              label={`${t("rowNoShow")} — ${t("colNightly")}`}
              value={val("noShowCutoffTime")}
              prefix={t("to")}
              suffix={t("nextDay")}
              error={e.noShowCutoffTime}
            />
          }
        />
        <RuleRow
          title={t("rowNoShowPenalty")}
          tip={help("hourlyNoShowPenalty", t("rowNoShowPenalty"))}
          hourly={
            <NumberCell
              name="hourlyNoShowPenaltyHours"
              label={`${t("rowNoShowPenalty")} — ${t("colHourly")}`}
              value={val("hourlyNoShowPenaltyHours")}
              min={0}
              max={24}
              unit={t("hoursUnit")}
              error={e.hourlyNoShowPenaltyHours}
            />
          }
          nightly={
            <NumberCell
              name="noShowPenaltyNights"
              label={`${t("rowNoShowPenalty")} — ${t("colNightly")}`}
              value={val("noShowPenaltyNights")}
              min={0}
              max={30}
              unit={t("nightsUnit")}
              error={e.noShowPenaltyNights}
            />
          }
        />
        <RuleRow
          title={t("rowHold")}
          tip={help("hourlyPendingHold", t("rowHold"))}
          hourly={
            <NumberCell
              name="hourlyPendingHoldMinutes"
              label={`${t("rowHold")} — ${t("colHourly")}`}
              value={val("hourlyPendingHoldMinutes")}
              min={5}
              max={1440}
              step={5}
              unit={t("minutesUnit")}
              error={e.hourlyPendingHoldMinutes}
            />
          }
          nightly={
            <NumberCell
              name="pendingHoldHours"
              label={`${t("rowHold")} — ${t("colNightly")}`}
              value={val("pendingHoldHours")}
              min={1}
              max={168}
              unit={t("hoursUnit")}
              error={e.pendingHoldHours}
            />
          }
        />
        <RuleRow
          title={t("rowEarly")}
          tip={help("hourlyRefundUnused", t("rowEarly"))}
          hourly={
            <label className="flex items-center gap-2 pt-1.5 text-sm">
              <Checkbox
                name="hourlyRefundUnused"
                value="true"
                defaultChecked={
                  settings.hourlyPolicy.refundUnusedOnEarlyDeparture
                }
                onCheckedChange={(checked) =>
                  setValues((prev) => ({
                    ...prev,
                    hourlyRefundUnused: checked,
                  }))
                }
              />
              {t("refundHours")}
            </label>
          }
          nightly={
            <NumberCell
              name="earlyDeparturePenaltyNights"
              label={`${t("rowEarly")} — ${t("colNightly")}`}
              value={val("earlyDeparturePenaltyNights")}
              min={0}
              max={30}
              unit={t("penaltyNights")}
              error={e.earlyDeparturePenaltyNights}
            />
          }
        />
      </div>

      <Toggle
        name="autoNoShowEnabled"
        label={t("autoNoShow")}
        defaultChecked={settings.autoNoShowEnabled}
        help={help("autoNoShow", t("autoNoShow"))}
      />

      <section
        aria-live="polite"
        className="grid gap-3 rounded-xl bg-secondary/60 p-4 text-sm"
        aria-labelledby="rules-summary"
      >
        <h3 id="rules-summary" className="font-heading text-base">
          {t("summary")}
        </h3>
        <div className="grid gap-4 md:grid-cols-2">
          {[
            { title: t("colHourly"), lines: text.hourly },
            { title: t("colNightly"), lines: text.nightly },
          ].map((group) => (
            <div key={group.title} className="grid content-start gap-1.5">
              <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
                {group.title}
              </p>
              <ul className="grid gap-1 pl-4 [&>li]:list-disc">
                {group.lines.map((line) => (
                  <li key={line}>{line}</li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      </section>

      <SectionFooter pending={pending} />
    </form>
  );
}

// ---------- Номера и уборка, оплата, сайт ----------

function SectionForm({
  section,
  settings,
  children,
}: {
  section: "rooms" | "payment" | "site";
  settings: HotelSettings;
  children: (e: Record<string, string>, v: Record<string, string>) => ReactNode;
}) {
  const t = useTranslations("Settings");
  const [state, action, pending] = useActionState<FormState, FormData>(
    saveSettingsSectionAction,
    {},
  );
  return (
    <form action={action} className="grid gap-4" noValidate>
      <input type="hidden" name="section" value={section} />
      <input type="hidden" name="version" value={settings.version} />
      <FormAlert state={state} success={t("savedSection")} />
      {children(state.fieldErrors ?? {}, state.values ?? {})}
      <SectionFooter pending={pending} />
    </form>
  );
}

export function RoomsCleaningForm({
  settings: s,
}: {
  settings: HotelSettings;
}) {
  const t = useTranslations("Settings");
  return (
    <SectionForm section="rooms" settings={s}>
      {(e, v) => (
        <>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field
              name="cleaningBufferMinutes"
              type="number"
              min={0}
              max={240}
              step={5}
              label={t("cleaningBuffer")}
              help={help("cleaningBuffer", t("cleaningBuffer"))}
              hint={t("cleaningBufferHint")}
              defaultValue={v.cleaningBufferMinutes ?? s.cleaningBufferMinutes}
              error={e.cleaningBufferMinutes}
            />
            <Field
              name="cleaningBufferNightlyMinutes"
              type="number"
              min={0}
              max={240}
              step={5}
              label={t("cleaningBufferNightly")}
              help={help("cleaningBufferNightly", t("cleaningBufferNightly"))}
              hint={t("cleaningBufferNightlyHint")}
              defaultValue={v.cleaningBufferNightlyMinutes ?? s.cleaningBufferNightlyMinutes}
              error={e.cleaningBufferNightlyMinutes}
            />
          </div>
          <p className="text-xs text-muted-foreground">{t("cleaningPerCategory")}</p>
          <Toggle
            name="autoReleaseRoomAfterCleaning"
            label={t("autoRelease")}
            defaultChecked={s.autoReleaseRoomAfterCleaning}
            help={help("autoRelease", t("autoRelease"))}
          />
        </>
      )}
    </SectionForm>
  );
}

export function PaymentForm({ settings: s }: { settings: HotelSettings }) {
  const t = useTranslations("Settings");
  return (
    <SectionForm section="payment" settings={s}>
      {(e, v) => (
        <>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field
              name="prepaymentPercent"
              type="number"
              min={0}
              max={100}
              label={t("prepayment")}
              help={help("prepayment", t("prepayment"))}
              hint={t("prepaymentHint")}
              defaultValue={v.prepaymentPercent ?? s.prepaymentPercent}
              error={e.prepaymentPercent}
            />
            <Field
              name="paymentHoldMinutes"
              type="number"
              min={5}
              max={1440}
              label={t("paymentHold")}
              help={help("paymentHold", t("paymentHold"))}
              hint={t("paymentHoldHint")}
              defaultValue={v.paymentHoldMinutes ?? s.paymentHoldMinutes}
              error={e.paymentHoldMinutes}
            />
          </div>
          <p className="rounded-xl bg-secondary px-3 py-2 text-sm text-muted-foreground">
            <strong className="font-medium text-foreground">
              {t("onlinePayment")}:
            </strong>{" "}
            {t("onlinePaymentOff")}
          </p>
        </>
      )}
    </SectionForm>
  );
}

export function SiteGuestsForm({ settings: s }: { settings: HotelSettings }) {
  const t = useTranslations("Settings");
  return (
    <SectionForm section="site" settings={s}>
      {() => (
        <>
          <Toggle
            name="autoConfirmWebBookings"
            label={t("autoConfirm")}
            defaultChecked={s.autoConfirmWebBookings}
            help={help("autoConfirm", t("autoConfirm"))}
          />
          <Toggle
            name="requireEmailConfirmation"
            label={t("requireEmail")}
            defaultChecked={s.requireEmailConfirmation}
            help={help("requireEmail", t("requireEmail"))}
          />
        </>
      )}
    </SectionForm>
  );
}

/** Метка «Заработает позже» у разделов, которые пока не применяются полностью. */
export function SoonBadge({ className }: { className?: string }) {
  const t = useTranslations("Settings");
  return (
    <span
      className={cn(
        "rounded-full bg-status-cleaning/12 px-2.5 py-0.5 text-xs font-medium text-status-cleaning",
        className,
      )}
    >
      {t("soonBadge")}
    </span>
  );
}

// ---------- Интеграции ----------

export function IntegrationForm({
  integration: i,
}: {
  integration: Integration;
}) {
  const t = useTranslations("Integrations");
  const [state, action, pending] = useActionState<FormState, FormData>(
    saveIntegrationAction,
    {},
  );
  const [testState, test, testing] = useActionState<FormState, FormData>(
    testIntegrationAction,
    {},
  );
  const canEnable = i.adapterAvailable && i.secretsConfigured;
  return (
    <div className="grid gap-4">
      <form action={action} className="grid gap-4">
        <input type="hidden" name="provider" value={i.provider} />
        <FormAlert state={state} success={t("saved")} />
        {i.allowedPublicKeys.length > 0 && (
          <div className="grid gap-4 sm:grid-cols-2">
            {i.allowedPublicKeys.map((key) => (
              <Field
                key={key}
                name={`config.${key}`}
                id={`${i.provider}-${key}`}
                label={t.has(key as never) ? t(key as never) : key}
                defaultValue={i.publicConfig[key] ?? ""}
                autoComplete="off"
              />
            ))}
          </div>
        )}
        <div className="flex flex-wrap items-end gap-4">
          <SelectField
            name="mode"
            id={`${i.provider}-mode`}
            label={t("mode")}
            defaultValue={i.mode}
            className="w-auto"
          >
            <option value="Sandbox">{t("Sandbox")}</option>
            <option value="Production">{t("Production")}</option>
          </SelectField>
          <label className="flex min-h-9 items-center gap-2 text-sm">
            <Checkbox
              name="isEnabled"
              value="true"
              defaultChecked={i.isEnabled}
              disabled={!canEnable && !i.isEnabled}
            />
            {t("enabled")}
          </label>
          <SubmitButton pending={pending} variant="outline">
            {t("save")}
          </SubmitButton>
        </div>
      </form>
      {(i.provider === "Email" || i.provider === "WhatsApp") && canEnable && (
        <form action={test} className="grid gap-2">
          <input type="hidden" name="provider" value={i.provider} />
          <FormAlert state={testState} success={i.provider === "WhatsApp" ? t("testWhatsAppDone") : t("testDone")} />
          <div>
            <SubmitButton pending={testing} variant="ghost">
              <Send aria-hidden />
              {i.provider === "WhatsApp" ? t("testWhatsApp") : t("test")}
            </SubmitButton>
          </div>
        </form>
      )}
    </div>
  );
}
