"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { useActionState } from "react";
import { PasswordField } from "@/components/forms/password-field";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import type { FormState } from "@/lib/api/problem";
import { resetPasswordAction } from "../actions";

export function ResetPasswordForm() {
  const t = useTranslations("Auth");
  const params = useSearchParams();
  const userId = params.get("userId");
  const token = params.get("token");
  const [state, action, pending] = useActionState<FormState, FormData>(resetPasswordAction, {});

  if (!userId || !token) return <p className="text-sm text-destructive">{t("invalidLink")}</p>;
  if (state.ok) {
    return (
      <div className="grid gap-3" role="status">
        <p className="text-sm">{t("resetDone")}</p>
        <Link href="/login" className="text-sm underline underline-offset-4">
          {t("toLogin")}
        </Link>
      </div>
    );
  }

  return (
    <form action={action} className="grid gap-4" noValidate>
      <input type="hidden" name="userId" value={userId} />
      <input type="hidden" name="token" value={token} />
      <FormAlert state={state} />
      <PasswordField
        name="password"
        label={t("newPassword")}
        autoComplete="new-password"
        hint={t("passwordHint")}
        required
        error={state.fieldErrors?.password}
      />
      <SubmitButton pending={pending}>{t("resetSubmit")}</SubmitButton>
    </form>
  );
}
