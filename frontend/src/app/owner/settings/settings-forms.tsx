"use client";

import { Send } from "lucide-react";
import { useTranslations } from "next-intl";
import { useActionState, type ReactNode } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { HelpTip } from "@/components/forms/help-tip";
import type messages from "../../../../messages/ru.json";
import { SelectField } from "@/components/forms/native-select";
import { SubmitButton } from "@/components/forms/submit-button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import type { Hotel, HotelSettings, Integration } from "@/lib/api/types";
import { saveHotelAction, saveIntegrationAction, saveSettingsAction, testIntegrationAction } from "./actions";

const textarea =
  "min-h-24 w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50";

export function HotelForm({ hotel }: { hotel: Hotel }) {
  const t = useTranslations("Settings");
  const [state, action, pending] = useActionState<FormState, FormData>(saveHotelAction, {});
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-4" noValidate>
      <input type="hidden" name="version" value={hotel.version} />
      <FormAlert state={state} success={t("saved")} />
      <Field name="name" label={t("name")} help={help("name", t("name"))} hint={t("nameHint")} required defaultValue={v.name ?? hotel.name} error={e.name} />
      <Field name="address" label={t("address")} help={help("address", t("address"))} defaultValue={v.address ?? hotel.address ?? ""} error={e.address} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field name="phone" type="tel" label={t("phone")} help={help("phone", t("phone"))} defaultValue={v.phone ?? hotel.phone ?? ""} error={e.phone} />
        <Field name="email" type="email" label={t("email")} help={help("email", t("email"))} defaultValue={v.email ?? hotel.email ?? ""} error={e.email} />
      </div>
      <div className="grid gap-1.5">
        <div className="flex items-center gap-1">
          <Label htmlFor="description">{t("aboutHotel")}</Label>
          {help("aboutHotel", t("aboutHotel"))}
        </div>
        <textarea id="description" name="description" className={textarea} defaultValue={v.description ?? hotel.description ?? ""} maxLength={2000} />
      </div>
      <div>
        <SubmitButton pending={pending}>{t("save")}</SubmitButton>
      </div>
    </form>
  );
}

export function SettingsForm({ settings: s }: { settings: HotelSettings }) {
  const t = useTranslations("Settings");
  const [state, action, pending] = useActionState<FormState, FormData>(saveSettingsAction, {});
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  const cp = s.cancellationPolicy;
  const hp = s.hourlyPolicy;
  return (
    <form action={action} className="grid gap-8" noValidate>
      <input type="hidden" name="version" value={s.version} />
      <FormAlert state={state} success={t("saved")} />

      <fieldset className="grid gap-4">
        <legend className="mb-2 font-heading text-lg">{t("rules")}</legend>
        <div className="grid gap-4 sm:grid-cols-3">
          <Field name="checkInTime" type="time" label={t("checkIn")} help={help("checkIn", t("checkIn"))} required defaultValue={v.checkInTime ?? s.checkInTime} error={e.checkInTime} />
          <Field name="checkOutTime" type="time" label={t("checkOut")} help={help("checkOut", t("checkOut"))} required defaultValue={v.checkOutTime ?? s.checkOutTime} error={e.checkOutTime} />
          <Field
            name="noShowCutoffTime"
            type="time"
            label={t("noShowCutoff")} help={help("noShowCutoff", t("noShowCutoff"))}
            hint={t("noShowCutoffHint")}
            required
            defaultValue={v.noShowCutoffTime ?? s.noShowCutoffTime}
            error={e.noShowCutoffTime}
          />
        </div>
        <div className="grid gap-1.5">
          <div className="flex items-center gap-1">
            <Label htmlFor="rules">{t("houseRules")}</Label>
            {help("houseRules", t("houseRules"))}
          </div>
          <textarea id="rules" name="rules" className={textarea} defaultValue={v.rules ?? s.rules ?? ""} maxLength={5000} />
        </div>
      </fieldset>

      <fieldset className="grid gap-4">
        <legend className="mb-2 font-heading text-lg">{t("bookings")}</legend>
        <div className="grid gap-4 sm:grid-cols-3">
          <Field name="prepaymentPercent" type="number" min={0} max={100} label={t("prepayment")} help={help("prepayment", t("prepayment"))} hint={t("prepaymentHint")} defaultValue={v.prepaymentPercent ?? s.prepaymentPercent} error={e.prepaymentPercent} />
          <Field name="pendingHoldHours" type="number" min={1} max={168} label={t("pendingHold")} help={help("pendingHold", t("pendingHold"))} hint={t("pendingHoldHint")} defaultValue={v.pendingHoldHours ?? s.pendingHoldHours} error={e.pendingHoldHours} />
          <Field name="paymentHoldMinutes" type="number" min={5} max={1440} label={t("paymentHold")} help={help("paymentHold", t("paymentHold"))} hint={t("paymentHoldHint")} defaultValue={v.paymentHoldMinutes ?? s.paymentHoldMinutes} error={e.paymentHoldMinutes} />
        </div>
        <Toggle name="autoConfirmWebBookings" label={t("autoConfirm")} help={help("autoConfirm", t("autoConfirm"))} defaultChecked={s.autoConfirmWebBookings} />
        <Toggle name="requireEmailConfirmation" label={t("requireEmail")} help={help("requireEmail", t("requireEmail"))} defaultChecked={s.requireEmailConfirmation} />
        <Toggle name="autoReleaseRoomAfterCleaning" label={t("autoRelease")} help={help("autoRelease", t("autoRelease"))} defaultChecked={s.autoReleaseRoomAfterCleaning} />
        <p className="rounded-xl bg-secondary px-3 py-2 text-sm text-muted-foreground">
          <strong className="font-medium text-foreground">{t("onlinePayment")}:</strong> {t("onlinePaymentOff")}
        </p>
      </fieldset>

      <fieldset className="grid gap-4">
        <legend className="mb-2 font-heading text-lg">{t("hourly")}</legend>
        <p className="-mt-2 text-sm text-muted-foreground">{t("hourlyHint")}</p>
        <div className="grid gap-4 sm:grid-cols-3">
          <Field name="hourlyMinHours" type="number" min={1} max={24} label={t("hourlyMin")} help={help("hourlyMin", t("hourlyMin"))} defaultValue={v.hourlyMinHours ?? s.hourlyMinHours} error={e.hourlyMinHours} />
          <Field name="hourlyMaxHours" type="number" min={1} max={24} label={t("hourlyMax")} help={help("hourlyMax", t("hourlyMax"))} hint={t("hourlyMaxHint")} defaultValue={v.hourlyMaxHours ?? s.hourlyMaxHours} error={e.hourlyMaxHours} />
          <Field name="cleaningBufferMinutes" type="number" min={0} max={240} step={5} label={t("cleaningBuffer")} help={help("cleaningBuffer", t("cleaningBuffer"))} hint={t("cleaningBufferHint")} defaultValue={v.cleaningBufferMinutes ?? s.cleaningBufferMinutes} error={e.cleaningBufferMinutes} />
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field name="hourlyFreeCancelMinutes" type="number" min={0} max={10080} step={15} label={t("hourlyFreeCancel")} help={help("hourlyFreeCancel", t("hourlyFreeCancel"))} hint={t("hourlyFreeCancelHint")} defaultValue={v.hourlyFreeCancelMinutes ?? hp.freeCancelMinutesBeforeStart} error={e.hourlyFreeCancelMinutes} />
          <Field name="hourlyLateCancelPenaltyHours" type="number" min={0} max={24} label={t("hourlyLateCancel")} help={help("hourlyLateCancel", t("hourlyLateCancel"))} hint={t("hourlyLateCancelHint")} defaultValue={v.hourlyLateCancelPenaltyHours ?? hp.lateCancelPenaltyHours} error={e.hourlyLateCancelPenaltyHours} />
          <Field name="hourlyPendingHoldMinutes" type="number" min={5} max={1440} step={5} label={t("hourlyPendingHold")} help={help("hourlyPendingHold", t("hourlyPendingHold"))} hint={t("hourlyPendingHoldHint")} defaultValue={v.hourlyPendingHoldMinutes ?? hp.pendingHoldMinutes} error={e.hourlyPendingHoldMinutes} />
          <Field name="hourlyNoShowAfterMinutes" type="number" min={5} max={720} step={5} label={t("hourlyNoShowAfter")} help={help("hourlyNoShowAfter", t("hourlyNoShowAfter"))} hint={t("hourlyNoShowAfterHint")} defaultValue={v.hourlyNoShowAfterMinutes ?? hp.noShowAfterMinutes} error={e.hourlyNoShowAfterMinutes} />
          <Field name="hourlyNoShowPenaltyHours" type="number" min={0} max={24} label={t("hourlyNoShowPenalty")} help={help("hourlyNoShowPenalty", t("hourlyNoShowPenalty"))} hint={t("hourlyNoShowPenaltyHint")} defaultValue={v.hourlyNoShowPenaltyHours ?? hp.noShowPenaltyHours} error={e.hourlyNoShowPenaltyHours} />
        </div>
        <Toggle name="hourlyRefundUnused" label={t("hourlyRefundUnused")} help={help("hourlyRefundUnused", t("hourlyRefundUnused"))} defaultChecked={hp.refundUnusedOnEarlyDeparture} />
      </fieldset>

      <fieldset className="grid gap-4">
        <legend className="mb-2 font-heading text-lg">{t("cancellation")}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field name="freeCancelHoursBeforeCheckIn" type="number" min={0} max={720} label={t("freeCancel")} help={help("freeCancel", t("freeCancel"))} hint={t("freeCancelHint")} defaultValue={v.freeCancelHoursBeforeCheckIn ?? cp.freeCancelHoursBeforeCheckIn} error={e.freeCancelHoursBeforeCheckIn} />
          <Field name="lateCancelPenaltyNights" type="number" min={0} max={30} label={t("lateCancel")} help={help("lateCancel", t("lateCancel"))} hint={t("lateCancelHint")} defaultValue={v.lateCancelPenaltyNights ?? cp.lateCancelPenaltyNights} error={e.lateCancelPenaltyNights} />
          <Field name="noShowPenaltyNights" type="number" min={0} max={30} label={t("noShowPenalty")} help={help("noShowPenalty", t("noShowPenalty"))} hint={t("noShowPenaltyHint")} defaultValue={v.noShowPenaltyNights ?? cp.noShowPenaltyNights} error={e.noShowPenaltyNights} />
          <Field name="earlyDeparturePenaltyNights" type="number" min={0} max={30} label={t("earlyDeparture")} help={help("earlyDeparture", t("earlyDeparture"))} hint={t("earlyDepartureHint")} defaultValue={v.earlyDeparturePenaltyNights ?? cp.earlyDeparturePenaltyNights} error={e.earlyDeparturePenaltyNights} />
        </div>
      </fieldset>

      <div>
        <SubmitButton pending={pending}>{t("save")}</SubmitButton>
      </div>
    </form>
  );
}

function Toggle({ name, label, defaultChecked, help }: { name: string; label: string; defaultChecked: boolean; help?: ReactNode }) {
  return (
    <div className="flex min-h-11 items-start gap-2 rounded-xl border bg-card px-3 py-2.5 text-sm">
      <label className="flex flex-1 items-start gap-3">
        <Checkbox name={name} value="true" defaultChecked={defaultChecked} className="mt-0.5" />
        {label}
      </label>
      {help}
    </div>
  );
}

type HelpKey = keyof (typeof messages)["SettingsHelp"];

/** Подсказка «?» к настройке: что делает, пример, когда заработает (если ещё не применяется). */
function SettingHelp({ helpKey, title }: { helpKey: HelpKey; title: string }) {
  const h = useTranslations("SettingsHelp");
  const key = (suffix: "text" | "example" | "pending") => `${helpKey}.${suffix}` as Parameters<typeof h>[0];
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

export function IntegrationForm({ integration: i }: { integration: Integration }) {
  const t = useTranslations("Integrations");
  const [state, action, pending] = useActionState<FormState, FormData>(saveIntegrationAction, {});
  const [testState, test, testing] = useActionState<FormState, FormData>(testIntegrationAction, {});
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
          <SelectField name="mode" id={`${i.provider}-mode`} label={t("mode")} defaultValue={i.mode} className="w-auto">
            <option value="Sandbox">{t("Sandbox")}</option>
            <option value="Production">{t("Production")}</option>
          </SelectField>
          <label className="flex min-h-9 items-center gap-2 text-sm">
            <Checkbox name="isEnabled" value="true" defaultChecked={i.isEnabled} disabled={!canEnable && !i.isEnabled} />
            {t("enabled")}
          </label>
          <SubmitButton pending={pending} variant="outline">
            {t("save")}
          </SubmitButton>
        </div>
      </form>
      {i.provider === "Email" && canEnable && (
        <form action={test} className="grid gap-2">
          <input type="hidden" name="provider" value={i.provider} />
          <FormAlert state={testState} success={t("testDone")} />
          <div>
            <SubmitButton pending={testing} variant="ghost">
              <Send aria-hidden />
              {t("test")}
            </SubmitButton>
          </div>
        </form>
      )}
    </div>
  );
}


