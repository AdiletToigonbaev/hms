"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { useActionState } from "react";
import { Field } from "@/components/forms/field";
import { PasswordField } from "@/components/forms/password-field";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import { registerAction } from "../actions";

export function RegisterForm() {
  const t = useTranslations("Auth");
  const [state, action, pending] = useActionState<FormState, FormData>(registerAction, {});
  // Пришли с брони на сайте — после подтверждения email и входа гость вернётся к ней.
  const next = useSearchParams().get("next") ?? "";
  const loginHref = next.startsWith("/") && !next.startsWith("//") ? `/login?next=${encodeURIComponent(next)}` : "/login";

  if (state.ok) {
    return (
      <div className="grid gap-3" role="status">
        <h2 className="font-medium">{t("registeredTitle")}</h2>
        <p className="text-sm text-muted-foreground">{t("registeredText")}</p>
        <Link href={loginHref} className="text-sm underline underline-offset-4">
          {t("toLogin")}
        </Link>
      </div>
    );
  }

  const errors = state.fieldErrors ?? {};
  const values = state.values ?? {};
  return (
    <form action={action} className="grid gap-4" noValidate>
      <input type="hidden" name="next" value={next} />
      <FormAlert state={state} />
      <Field name="fullName" label={t("fullName")} autoComplete="name" required defaultValue={values.fullName} error={errors.fullName} />
      <Field name="email" type="email" label={t("email")} autoComplete="email" required defaultValue={values.email} error={errors.email} />
      <Field name="phone" type="tel" label={t("phone")} autoComplete="tel" hint={t("phoneHint")} defaultValue={values.phone} error={errors.phone} />
      <PasswordField
        name="password"
        label={t("password")}
        autoComplete="new-password"
        hint={t("passwordHint")}
        required
        error={errors.password}
      />
      <div className="grid gap-1.5">
        <div className="flex items-start gap-2">
          <Checkbox id="acceptPersonalData" name="acceptPersonalData" value="true" defaultChecked={values.acceptPersonalData !== undefined} aria-invalid={errors.acceptPersonalData ? true : undefined} />
          <Label htmlFor="acceptPersonalData" className="leading-snug font-normal">
            {t("acceptPersonalData")}
          </Label>
        </div>
        {errors.acceptPersonalData && <p className="text-xs text-destructive">{errors.acceptPersonalData}</p>}
      </div>
      <div className="flex items-start gap-2">
        <Checkbox id="acceptWhatsApp" name="acceptWhatsApp" value="true" defaultChecked={values.acceptWhatsApp !== undefined} />
        <Label htmlFor="acceptWhatsApp" className="leading-snug font-normal">
          {t("acceptWhatsApp")}
        </Label>
      </div>
      <SubmitButton pending={pending}>{t("submitRegister")}</SubmitButton>
      <p className="text-sm">
        {t("haveAccount")}{" "}
        <Link href={loginHref} className="underline underline-offset-4">
          {t("toLogin")}
        </Link>
      </p>
    </form>
  );
}
