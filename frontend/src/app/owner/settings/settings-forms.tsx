"use client";

import { Send } from "lucide-react";
import { useTranslations } from "next-intl";
import { useActionState } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
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
      <Field name="name" label={t("name")} hint={t("nameHint")} required defaultValue={v.name ?? hotel.name} error={e.name} />
      <Field name="address" label={t("address")} defaultValue={v.address ?? hotel.address ?? ""} error={e.address} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field name="phone" type="tel" label={t("phone")} defaultValue={v.phone ?? hotel.phone ?? ""} error={e.phone} />
        <Field name="email" type="email" label={t("email")} defaultValue={v.email ?? hotel.email ?? ""} error={e.email} />
      </div>
      <div className="grid gap-1.5">
        <Label htmlFor="description">{t("aboutHotel")}</Label>
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
  return (
    <form action={action} className="grid gap-8" noValidate>
      <input type="hidden" name="version" value={s.version} />
      <FormAlert state={state} success={t("saved")} />

      <fieldset className="grid gap-4">
        <legend className="mb-2 font-heading text-lg">{t("rules")}</legend>
        <div className="grid gap-4 sm:grid-cols-3">
          <Field name="checkInTime" type="time" label={t("checkIn")} required defaultValue={v.checkInTime ?? s.checkInTime} error={e.checkInTime} />
          <Field name="checkOutTime" type="time" label={t("checkOut")} required defaultValue={v.checkOutTime ?? s.checkOutTime} error={e.checkOutTime} />
          <Field
            name="noShowCutoffTime"
            type="time"
            label={t("noShowCutoff")}
            hint={t("noShowCutoffHint")}
            required
            defaultValue={v.noShowCutoffTime ?? s.noShowCutoffTime}
            error={e.noShowCutoffTime}
          />
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="rules">{t("houseRules")}</Label>
          <textarea id="rules" name="rules" className={textarea} defaultValue={v.rules ?? s.rules ?? ""} maxLength={5000} />
        </div>
      </fieldset>

      <fieldset className="grid gap-4">
        <legend className="mb-2 font-heading text-lg">{t("bookings")}</legend>
        <div className="grid gap-4 sm:grid-cols-3">
          <Field name="prepaymentPercent" type="number" min={0} max={100} label={t("prepayment")} hint={t("prepaymentHint")} defaultValue={v.prepaymentPercent ?? s.prepaymentPercent} error={e.prepaymentPercent} />
          <Field name="pendingHoldHours" type="number" min={1} max={168} label={t("pendingHold")} hint={t("pendingHoldHint")} defaultValue={v.pendingHoldHours ?? s.pendingHoldHours} error={e.pendingHoldHours} />
          <Field name="paymentHoldMinutes" type="number" min={5} max={1440} label={t("paymentHold")} hint={t("paymentHoldHint")} defaultValue={v.paymentHoldMinutes ?? s.paymentHoldMinutes} error={e.paymentHoldMinutes} />
        </div>
        <Toggle name="autoConfirmWebBookings" label={t("autoConfirm")} defaultChecked={s.autoConfirmWebBookings} />
        <Toggle name="requireEmailConfirmation" label={t("requireEmail")} defaultChecked={s.requireEmailConfirmation} />
        <Toggle name="autoReleaseRoomAfterCleaning" label={t("autoRelease")} defaultChecked={s.autoReleaseRoomAfterCleaning} />
        <p className="rounded-xl bg-secondary px-3 py-2 text-sm text-muted-foreground">
          <strong className="font-medium text-foreground">{t("onlinePayment")}:</strong> {t("onlinePaymentOff")}
        </p>
      </fieldset>

      <fieldset className="grid gap-4">
        <legend className="mb-2 font-heading text-lg">{t("cancellation")}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field name="freeCancelHoursBeforeCheckIn" type="number" min={0} max={720} label={t("freeCancel")} hint={t("freeCancelHint")} defaultValue={v.freeCancelHoursBeforeCheckIn ?? cp.freeCancelHoursBeforeCheckIn} error={e.freeCancelHoursBeforeCheckIn} />
          <Field name="lateCancelPenaltyNights" type="number" min={0} max={30} label={t("lateCancel")} hint={t("lateCancelHint")} defaultValue={v.lateCancelPenaltyNights ?? cp.lateCancelPenaltyNights} error={e.lateCancelPenaltyNights} />
          <Field name="noShowPenaltyNights" type="number" min={0} max={30} label={t("noShowPenalty")} hint={t("noShowPenaltyHint")} defaultValue={v.noShowPenaltyNights ?? cp.noShowPenaltyNights} error={e.noShowPenaltyNights} />
          <Field name="earlyDeparturePenaltyNights" type="number" min={0} max={30} label={t("earlyDeparture")} hint={t("earlyDepartureHint")} defaultValue={v.earlyDeparturePenaltyNights ?? cp.earlyDeparturePenaltyNights} error={e.earlyDeparturePenaltyNights} />
        </div>
      </fieldset>

      <div>
        <SubmitButton pending={pending}>{t("save")}</SubmitButton>
      </div>
    </form>
  );
}

function Toggle({ name, label, defaultChecked }: { name: string; label: string; defaultChecked: boolean }) {
  return (
    <label className="flex min-h-11 items-start gap-3 rounded-xl border bg-card px-3 py-2.5 text-sm">
      <Checkbox name={name} value="true" defaultChecked={defaultChecked} className="mt-0.5" />
      {label}
    </label>
  );
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


