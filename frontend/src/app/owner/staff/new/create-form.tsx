"use client";

import { useTranslations } from "next-intl";
import { useActionState } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import { createStaffAction } from "../actions";

export function CreateStaffForm() {
  const t = useTranslations("Staff");
  const auth = useTranslations("Auth");
  const [state, action, pending] = useActionState<FormState, FormData>(createStaffAction, {});
  const errors = state.fieldErrors ?? {};
  const values = state.values ?? {};

  return (
    <form action={action} className="grid gap-4" noValidate>
      <FormAlert state={state} />
      <Field name="fullName" label={t("fullName")} required defaultValue={values.fullName} error={errors.fullName} />
      <Field name="email" type="email" label={auth("email")} defaultValue={values.email} error={errors.email} />
      <Field name="phone" type="tel" label={auth("phone")} hint={auth("phoneHint")} defaultValue={values.phone} error={errors.phone} />
      <div className="grid gap-1.5">
        <Label htmlFor="role">{t("role")}</Label>
        <select
          id="role"
          name="role"
          key={values.role ?? "Administrator"}
          defaultValue={values.role ?? "Administrator"}
          className="h-8 rounded-lg border border-input bg-transparent px-2.5 text-sm"
        >
          <option value="Administrator">{t("roleAdministrator")}</option>
          <option value="Housekeeper">{t("roleHousekeeper")}</option>
        </select>
        {errors.role && <p className="text-xs text-destructive">{errors.role}</p>}
      </div>
      <Field
        name="initialPassword"
        type="text"
        autoComplete="off"
        label={t("initialPassword")}
        hint={auth("passwordHint")}
        required
        error={errors.initialPassword}
      />
      <SubmitButton pending={pending}>{t("create")}</SubmitButton>
    </form>
  );
}
