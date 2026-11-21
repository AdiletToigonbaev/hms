"use client";

import { useTranslations } from "next-intl";
import { useActionState, useEffect, useState } from "react";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import type { StaySearch } from "@/lib/site/stay";
import { createWebBookingAction } from "./actions";

const textarea =
  "min-h-20 w-full rounded-lg border border-input bg-card px-2.5 py-2 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 md:text-sm";

function newKey() {
  return crypto.randomUUID().replace(/-/g, "");
}

/** Подтверждение брони гостем: пожелания и промокод. Повторное нажатие не создаст вторую бронь (ключ идемпотентности). */
export function WebBookingForm({ stay, autoConfirm }: { stay: StaySearch; autoConfirm: boolean }) {
  const t = useTranslations("Site");
  const [state, action, pending] = useActionState<FormState, FormData>(createWebBookingAction.bind(null, JSON.stringify(stay)), {});
  const [key, setKey] = useState(newKey);
  useEffect(() => {
    // Новый ключ после отказа API: исправленная попытка — новый запрос.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (state.ok === false) setKey(newKey());
  }, [state]);
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-4" noValidate>
      <input type="hidden" name="idempotencyKey" value={key} />
      <FormAlert state={state} />
      <div className="grid gap-1.5">
        <Label htmlFor="comment">{t("comment")}</Label>
        <textarea id="comment" name="comment" maxLength={500} defaultValue={v.comment} placeholder={t("commentPlaceholder")} className={textarea} />
      </div>
      <div className="grid gap-1.5 sm:max-w-60">
        <Label htmlFor="promoCode">{t("promo")}</Label>
        <Input id="promoCode" name="promoCode" maxLength={50} defaultValue={v.promoCode} autoComplete="off" className="h-11" />
      </div>
      <p className="text-sm text-muted-foreground">{autoConfirm ? t("confirmAuto") : t("confirmManual")}</p>
      <div>
        <SubmitButton pending={pending} size="lg" className="h-12 w-full px-8 text-base sm:w-auto">
          {t("bookNow")}
        </SubmitButton>
      </div>
    </form>
  );
}
