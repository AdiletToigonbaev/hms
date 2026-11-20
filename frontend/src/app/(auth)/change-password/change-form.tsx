"use client";

import { useTranslations } from "next-intl";
import { useActionState } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import type { FormState } from "@/lib/api/problem";
import { changePasswordAction } from "../actions";

export function ChangePasswordForm() {
  const t = useTranslations("Auth");
  const [state, action, pending] = useActionState<FormState, FormData>(changePasswordAction, {});
  return (
    <form action={action} className="grid gap-4" noValidate>
      <FormAlert state={state} />
      <Field
        name="currentPassword"
        type="password"
        label={t("currentPassword")}
        autoComplete="current-password"
        required
        error={state.fieldErrors?.currentPassword}
      />
      <Field
        name="newPassword"
        type="password"
        label={t("newPassword")}
        autoComplete="new-password"
        hint={t("passwordHint")}
        required
        error={state.fieldErrors?.newPassword}
      />
      <SubmitButton pending={pending}>{t("changeSubmit")}</SubmitButton>
    </form>
  );
}
