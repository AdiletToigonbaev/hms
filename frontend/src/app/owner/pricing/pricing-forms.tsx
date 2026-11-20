"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { useActionState } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { SelectField } from "@/components/forms/native-select";
import { SubmitButton } from "@/components/forms/submit-button";
import { buttonVariants } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import type { FormState } from "@/lib/api/problem";
import type { Discount, ExtraService, Rate, RoomType } from "@/lib/api/types";
import { ALL_DAYS, weekDays } from "@/lib/bookings/status";
import { deleteRateAction, saveDiscountAction, saveRateAction, saveServiceAction, toggleActiveAction } from "./actions";

function CancelEdit({ editing }: { editing: boolean }) {
  const t = useTranslations("Pricing");
  if (!editing) return null;
  return (
    <Link href="/owner/pricing" className={buttonVariants({ variant: "ghost" })}>
      {t("cancelEdit")}
    </Link>
  );
}

export function RateForm({ rate, types }: { rate?: Rate; types: RoomType[] }) {
  const t = useTranslations("Pricing");
  const [state, action, pending] = useActionState<FormState, FormData>(saveRateAction, {});
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  const mask = rate?.daysOfWeek ?? ALL_DAYS;
  return (
    <form action={action} className="grid gap-4" noValidate>
      {rate && <input type="hidden" name="id" value={rate.id} />}
      <FormAlert state={state} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field id="rate-name" name="name" label={t("name")} required placeholder="Новогодний сезон" defaultValue={v.name ?? rate?.name} error={e.name} />
        <SelectField id="rate-roomTypeId" name="roomTypeId" label={t("roomType")} defaultValue={v.roomTypeId ?? rate?.roomTypeId ?? ""} error={e.roomTypeId} key={v.roomTypeId ?? rate?.roomTypeId}>
          <option value="" disabled>
            —
          </option>
          {types.map((type) => (
            <option key={type.id} value={type.id}>
              {type.name}
            </option>
          ))}
        </SelectField>
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
        <SelectField id="rate-unit" name="unit" label={t("unit")} defaultValue={v.unit ?? rate?.unit ?? "Night"} key={v.unit ?? rate?.unit} error={e.unit}>
          <option value="Night">{t("unitNight")}</option>
          <option value="Hour">{t("unitHour")}</option>
        </SelectField>
        <Field id="rate-dateFrom" name="dateFrom" type="date" label={t("dateFrom")} required defaultValue={v.dateFrom ?? rate?.dateFrom} error={e.dateFrom} />
        <Field id="rate-dateTo" name="dateTo" type="date" label={t("dateTo")} required defaultValue={v.dateTo ?? rate?.dateTo} error={e.dateTo} />
        <Field id="rate-price" name="price" inputMode="decimal" label={t("rateprice")} required className="tabular" defaultValue={v.price ?? rate?.price} error={e.price} />
        <Field id="rate-priority" name="priority" type="number" min={0} max={100} label={t("priority")} defaultValue={v.priority ?? rate?.priority ?? 1} error={e.priority} />
      </div>
      <p className="-mt-2 text-xs text-muted-foreground">{t("priorityHint")}</p>
      <fieldset className="grid gap-2" aria-describedby={e.days ? "days-error" : undefined}>
        <legend className="mb-1 text-sm font-medium">{t("days")}</legend>
        <div className="flex flex-wrap gap-x-4 gap-y-2">
          {weekDays.map((d) => (
            <label key={d.bit} className="flex items-center gap-1.5 text-sm">
              <Checkbox name="days" value={String(d.bit)} defaultChecked={(mask & d.bit) !== 0} />
              {d.short}
            </label>
          ))}
        </div>
        {e.days && (
          <p id="days-error" className="text-xs text-destructive">
            {e.days}
          </p>
        )}
      </fieldset>
      <label className="flex items-center gap-2 text-sm">
        <Checkbox name="isActive" value="true" defaultChecked={rate?.isActive ?? true} />
        {t("isActive")}
      </label>
      <div className="flex gap-2">
        <SubmitButton pending={pending}>{rate ? t("save") : t("create")}</SubmitButton>
        <CancelEdit editing={Boolean(rate)} />
      </div>
    </form>
  );
}

export function DiscountForm({ discount }: { discount?: Discount }) {
  const t = useTranslations("Pricing");
  const [state, action, pending] = useActionState<FormState, FormData>(saveDiscountAction, {});
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-4" noValidate>
      {discount && <input type="hidden" name="id" value={discount.id} />}
      <FormAlert state={state} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field id="discount-name" name="name" label={t("name")} required placeholder="Постоянный гость" defaultValue={v.name ?? discount?.name} error={e.name} />
        <Field id="discount-code" name="code" label={t("code")} className="uppercase" autoComplete="off" defaultValue={v.code ?? discount?.code ?? ""} error={e.code} hint={t("codeHint")} />
      </div>
      <div className="grid gap-4 sm:grid-cols-3 lg:grid-cols-6">
        <SelectField id="discount-type" name="type" label={t("type")} defaultValue={v.type ?? discount?.type ?? "Percent"} error={e.type} key={v.type ?? discount?.type}>
          <option value="Percent">{t("percent")}</option>
          <option value="Amount">{t("amount")}</option>
        </SelectField>
        <Field id="discount-value" name="value" inputMode="decimal" label={t("value")} required className="tabular" defaultValue={v.value ?? discount?.value} error={e.value} />
        <Field id="discount-validFrom" name="validFrom" type="date" label={t("validFrom")} defaultValue={v.validFrom ?? discount?.validFrom ?? ""} error={e.validFrom} />
        <Field id="discount-validTo" name="validTo" type="date" label={t("validTo")} defaultValue={v.validTo ?? discount?.validTo ?? ""} error={e.validTo} />
        <Field id="discount-minNights" name="minNights" type="number" min={0} max={365} label={t("minNights")} defaultValue={v.minNights ?? discount?.minNights ?? 0} error={e.minNights} />
        <Field id="discount-minHours" name="minHours" type="number" min={0} max={24} label={t("minHours")} defaultValue={v.minHours ?? discount?.minHours ?? 0} error={e.minHours} />
      </div>
      <div className="flex gap-2">
        <SubmitButton pending={pending}>{discount ? t("save") : t("create")}</SubmitButton>
        <CancelEdit editing={Boolean(discount)} />
      </div>
    </form>
  );
}

export function ServiceForm({ service }: { service?: ExtraService }) {
  const t = useTranslations("Pricing");
  const [state, action, pending] = useActionState<FormState, FormData>(saveServiceAction, {});
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-4" noValidate>
      {service && <input type="hidden" name="id" value={service.id} />}
      <FormAlert state={state} />
      <div className="grid gap-4 sm:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <Field id="service-name" name="name" label={t("name")} required placeholder="Завтрак" defaultValue={v.name ?? service?.name} error={e.name} />
        <Field id="service-price" name="price" inputMode="decimal" label={t("servicePrice")} required className="tabular" defaultValue={v.price ?? service?.price} error={e.price} />
      </div>
      <Field id="service-description" name="description" label={t("descriptionLabel")} defaultValue={v.description ?? service?.description ?? ""} error={e.description} />
      <div className="flex flex-wrap gap-x-6 gap-y-2">
        <label className="flex items-center gap-2 text-sm">
          <Checkbox name="isPerNight" value="true" defaultChecked={service?.isPerNight ?? false} />
          {t("perNight")}
        </label>
        <label className="flex items-center gap-2 text-sm">
          <Checkbox name="isPublic" value="true" defaultChecked={service?.isPublic ?? true} />
          {t("isPublic")}
        </label>
      </div>
      <div className="flex gap-2">
        <SubmitButton pending={pending}>{service ? t("save") : t("create")}</SubmitButton>
        <CancelEdit editing={Boolean(service)} />
      </div>
    </form>
  );
}

export function DeleteRateButton({ id, name }: { id: string; name: string }) {
  const t = useTranslations("Pricing");
  const [state, action, pending] = useActionState<FormState, FormData>(deleteRateAction, {});
  return (
    <form action={action} className="inline">
      <input type="hidden" name="id" value={id} />
      <SubmitButton pending={pending} variant="ghost" size="sm" aria-label={`${t("delete")}: ${name}`}>
        {t("delete")}
      </SubmitButton>
      {state.error && <FormAlert state={state} />}
    </form>
  );
}

export function ToggleActiveButton({ id, kind, active, name }: { id: string; kind: "discounts" | "services"; active: boolean; name: string }) {
  const t = useTranslations("Pricing");
  const [state, action, pending] = useActionState<FormState, FormData>(toggleActiveAction, {});
  const label = active ? t("deactivate") : t("activate");
  return (
    <form action={action} className="inline">
      <input type="hidden" name="id" value={id} />
      <input type="hidden" name="kind" value={kind} />
      <input type="hidden" name="active" value={String(!active)} />
      <SubmitButton pending={pending} variant="ghost" size="sm" aria-label={`${label}: ${name}`}>
        {label}
      </SubmitButton>
      {state.error && <FormAlert state={state} />}
    </form>
  );
}
