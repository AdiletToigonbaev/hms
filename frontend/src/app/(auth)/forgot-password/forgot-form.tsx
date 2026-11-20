"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { useActionState } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import type { FormState } from "@/lib/api/problem";
import { forgotPasswordAction } from "../actions";

export function ForgotPasswordForm() {
  const t = useTranslations("Auth");
  const [state, action, pending] = useActionState<FormState, FormData>(forgotPasswordAction, {});
  return (
    <form action={action} className="grid gap-4" noValidate>
      <FormAlert state={state} success={t("forgotSent")} />
      {!state.ok && (
        <>
          <Field name="email" type="email" label={t("email")} autoComplete="email" required defaultValue={state.values?.email} error={state.fieldErrors?.email} />
          <SubmitButton pending={pending}>{t("forgotSubmit")}</SubmitButton>
        </>
      )}
      <Link href="/login" className="text-sm underline underline-offset-4">
        {t("toLogin")}
      </Link>
    </form>
  );
}
