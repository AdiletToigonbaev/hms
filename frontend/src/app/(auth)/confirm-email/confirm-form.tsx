"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { useActionState } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import type { FormState } from "@/lib/api/problem";
import { confirmEmailAction, resendConfirmationAction } from "../actions";

/**
 * Подтверждение — по нажатию кнопки, а не при открытии ссылки: почтовые сканеры,
 * открывающие ссылки автоматически, не должны подтверждать адрес за пользователя.
 */
export function ConfirmEmailForm() {
  const t = useTranslations("Auth");
  const params = useSearchParams();
  const userId = params.get("userId");
  const token = params.get("token");
  const [state, action, pending] = useActionState<FormState, FormData>(confirmEmailAction, {});

  if (!userId || !token) return <p className="text-sm text-destructive">{t("invalidLink")}</p>;
  if (state.ok) {
    return (
      <div className="grid gap-3" role="status">
        <p className="text-sm">{t("confirmedText")}</p>
        <Link href="/login" className="text-sm underline underline-offset-4">
          {t("toLogin")}
        </Link>
      </div>
    );
  }

  return (
    <form action={action} className="grid gap-4">
      <input type="hidden" name="userId" value={userId} />
      <input type="hidden" name="token" value={token} />
      <FormAlert state={state} />
      <p className="text-sm text-muted-foreground">{t("confirmText")}</p>
      <SubmitButton pending={pending}>{t("confirmSubmit")}</SubmitButton>
    </form>
  );
}

export function ResendConfirmationForm() {
  const t = useTranslations("Auth");
  const [state, action, pending] = useActionState<FormState, FormData>(resendConfirmationAction, {});
  return (
    <form action={action} className="grid gap-4" noValidate>
      <FormAlert state={state} success={t("resentText")} />
      <Field name="email" type="email" label={t("email")} autoComplete="email" required defaultValue={state.values?.email} error={state.fieldErrors?.email} />
      <SubmitButton pending={pending} variant="outline">
        {t("resendSubmit")}
      </SubmitButton>
    </form>
  );
}
