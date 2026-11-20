"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { useActionState } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import type { FormState } from "@/lib/api/problem";
import { loginAction } from "../actions";

export function LoginForm() {
  const t = useTranslations("Auth");
  const next = useSearchParams().get("next") ?? "";
  const [state, action, pending] = useActionState<FormState, FormData>(loginAction, {});

  return (
    <form action={action} className="grid gap-4" noValidate>
      <input type="hidden" name="next" value={next} />
      <FormAlert state={state} />
      <Field name="login" label={t("login")} autoComplete="username" required error={state.fieldErrors?.login} />
      <Field
        name="password"
        type="password"
        label={t("password")}
        autoComplete="current-password"
        required
        error={state.fieldErrors?.password}
      />
      <SubmitButton pending={pending}>{t("submitLogin")}</SubmitButton>
      <div className="flex flex-wrap justify-between gap-2 text-sm">
        <Link href="/forgot-password" className="underline underline-offset-4">
          {t("forgot")}
        </Link>
        <span>
          {t("noAccount")}{" "}
          <Link href="/register" className="underline underline-offset-4">
            {t("toRegister")}
          </Link>
        </span>
      </div>
    </form>
  );
}
