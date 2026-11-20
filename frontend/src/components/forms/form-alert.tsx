"use client";

import { CircleAlert, CircleCheck } from "lucide-react";
import { useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { errorKey, type FormState } from "@/lib/api/problem";

/** Общая ошибка или успех серверного действия. */
export function FormAlert({ state, success }: { state: FormState; success?: string }) {
  const t = useTranslations("Errors");
  if (state.ok && success) {
    return (
      <Alert role="status">
        <CircleCheck aria-hidden />
        <AlertDescription>{success}</AlertDescription>
      </Alert>
    );
  }
  if (!state.error) return null;
  const key = errorKey(state.error) as Parameters<typeof t>[0];
  return (
    <Alert variant="destructive" role="alert">
      <CircleAlert aria-hidden />
      <AlertDescription>{state.message ?? (t.has(key) ? t(key) : t("unknown"))}</AlertDescription>
    </Alert>
  );
}
