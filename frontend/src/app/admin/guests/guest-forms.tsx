"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { useActionState, useRef, useState } from "react";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { SelectField } from "@/components/forms/native-select";
import { SubmitButton } from "@/components/forms/submit-button";
import { buttonVariants } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import type { ConsentState, ConsentType, Guest } from "@/lib/api/types";
import { formatHotelDateTime } from "@/lib/format";
import { cn } from "@/lib/utils";
import { anonymizeGuestAction, saveGuestAction, setConsentAction } from "./actions";

const textarea =
  "min-h-20 w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50";

export function GuestForm({ guest }: { guest?: Guest }) {
  const t = useTranslations("Guests");
  const [state, action, pending] = useActionState<FormState, FormData>(saveGuestAction, {});
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-4" noValidate>
      {guest && <input type="hidden" name="id" value={guest.id} />}
      {guest && <input type="hidden" name="version" value={guest.version} />}
      <FormAlert state={state} success={t("saved")} />
      {state.error === "guest.duplicate" && state.refId && (
        <Link href={`/admin/guests/${state.refId}`} className={cn(buttonVariants({ variant: "outline", size: "sm" }), "w-fit")}>
          {t("openExisting")}
        </Link>
      )}
      <Field name="fullName" label={t("fullName")} required autoComplete="off" defaultValue={v.fullName ?? guest?.fullName} error={e.fullName} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field name="phone" type="tel" inputMode="tel" label={t("phone")} placeholder="+996 XXX XXX XXX" hint={t("contactHint")} defaultValue={v.phone ?? guest?.phone ?? ""} error={e.phone} />
        <Field name="email" type="email" label={t("email")} defaultValue={v.email ?? guest?.email ?? ""} error={e.email} />
        <Field name="whatsAppPhone" type="tel" inputMode="tel" label={t("whatsAppPhone")} defaultValue={v.whatsAppPhone ?? guest?.whatsAppPhone ?? ""} error={e.whatsAppPhone} />
        <Field name="birthDate" type="date" label={t("birthDate")} defaultValue={v.birthDate ?? guest?.birthDate ?? ""} error={e.birthDate} />
      </div>
      <div className="grid gap-1.5">
        <Label htmlFor="notes">{t("notes")}</Label>
        <textarea id="notes" name="notes" className={textarea} maxLength={2000} defaultValue={v.notes ?? guest?.notes ?? ""} aria-describedby="notes-hint" />
        <p id="notes-hint" className="text-xs text-muted-foreground">
          {t("notesHint")}
        </p>
      </div>
      <div>
        <SubmitButton pending={pending}>{guest ? t("save") : t("create")}</SubmitButton>
      </div>
    </form>
  );
}

const sources = ["FrontDesk", "Phone", "WhatsApp", "Written", "Portal"] as const;
const sourceKey = { Portal: "srcPortal", FrontDesk: "srcFrontDesk", Phone: "srcPhone", WhatsApp: "srcWhatsApp", Written: "srcWritten" } as const;

/** Согласие одного вида: текущее состояние и отметка согласия/отзыва с источником. */
export function ConsentForm({ guestId, type, current }: { guestId: string; type: ConsentType; current?: ConsentState }) {
  const t = useTranslations("Guests");
  const [state, action, pending] = useActionState<FormState, FormData>(setConsentAction, {});
  const granted = current?.granted ?? false;
  const title = type === "PersonalData" ? t("consentPersonalData") : t("consentWhatsApp");
  return (
    <form action={action} className="grid gap-3 rounded-xl border p-3">
      <input type="hidden" name="id" value={guestId} />
      <input type="hidden" name="type" value={type} />
      <input type="hidden" name="granted" value={String(!granted)} />
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-sm font-medium">{title}</span>
        <span
          className={cn(
            "rounded-full px-2.5 py-0.5 text-xs font-medium",
            current ? (granted ? "bg-status-available/12 text-status-available" : "bg-status-out/12 text-status-out") : "bg-secondary text-muted-foreground",
          )}
        >
          {current ? (granted ? t("granted") : t("revoked")) : t("notRecorded")}
        </span>
      </div>
      {current && (
        <p className="text-xs text-muted-foreground">
          {t(sourceKey[current.source])} · {formatHotelDateTime(current.recordedAt)}
        </p>
      )}
      <FormAlert state={state} />
      <div className="flex flex-wrap items-end gap-2">
        <SelectField name="source" id={`${type}-source`} label={t("source")} defaultValue="FrontDesk" className="w-auto" error={state.fieldErrors?.source}>
          {sources.map((s) => (
            <option key={s} value={s}>
              {t(sourceKey[s])}
            </option>
          ))}
        </SelectField>
        <SubmitButton pending={pending} variant="outline" size="sm" className="h-9">
          {granted ? t("revoke") : t("grant")}
        </SubmitButton>
      </div>
    </form>
  );
}

/** Обезличивание — только владелец; через окно подтверждения. */
export function AnonymizeForm({ guest }: { guest: Guest }) {
  const t = useTranslations("Guests");
  const [state, action, pending] = useActionState<FormState, FormData>(anonymizeGuestAction, {});
  const formRef = useRef<HTMLFormElement>(null);
  const [asking, setAsking] = useState(false);
  return (
    <form ref={formRef} action={action} className="grid gap-3">
      <input type="hidden" name="id" value={guest.id} />
      <FormAlert state={state} />
      <p className="text-sm text-muted-foreground">{guest.hasAccount ? t("hasAccountHint") : t("dangerText")}</p>
      <div>
        <SubmitButton
          pending={pending}
          variant="destructive"
          disabled={guest.hasAccount}
          onClick={(e) => {
            e.preventDefault();
            setAsking(true);
          }}
        >
          {t("anonymize")}
        </SubmitButton>
      </div>
      <ConfirmDialog
        open={asking}
        onOpenChange={setAsking}
        title={t("anonymizeConfirmTitle", { name: guest.fullName })}
        description={t("anonymizeConfirmText")}
        confirmLabel={t("anonymizeYes")}
        cancelLabel={t("anonymizeNo")}
        destructive
        onConfirm={() => formRef.current?.requestSubmit()}
      />
    </form>
  );
}
