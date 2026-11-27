"use client";

import { RotateCcw } from "lucide-react";
import { useTranslations } from "next-intl";
import { useActionState, useEffect, useState } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { SelectField } from "@/components/forms/native-select";
import { SubmitButton } from "@/components/forms/submit-button";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import type { MessageChannel, SendOptions } from "@/lib/api/types";
import { windowLeft } from "@/lib/messaging/labels";
import { resolveConversationAction, retryMessageAction, sendMessageAction } from "./actions";

const textarea =
  "min-h-24 w-full rounded-lg border border-input bg-card px-2.5 py-2 text-base outline-none md:text-sm focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50";

function newKey() {
  return crypto.randomUUID().replace(/-/g, "");
}

/** Ключ идемпотентности: повтор (двойной клик) уходит с тем же ключом; после ответа API — новый. */
function useIdempotencyKey(state: FormState) {
  const [key, setKey] = useState(newKey);
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (state.ok !== undefined) setKey(newKey());
  }, [state]);
  return key;
}

interface SendMessageFormProps {
  options: SendOptions;
  guestId?: string | null;
  conversationId?: string | null;
  bookingId?: string | null;
  /** Только WhatsApp (ответ в диалоге). */
  whatsAppOnly?: boolean;
}

/**
 * Ручная отправка гостю (ТЗ §11). WhatsApp: в окне 24 ч после сообщения гостя — свободный текст,
 * вне окна — только одобренный шаблон «Сообщение от администратора» и при согласии гостя (проверяет API).
 */
export function SendMessageForm({ options, guestId, conversationId, bookingId, whatsAppOnly }: SendMessageFormProps) {
  const t = useTranslations("Messages");
  const [state, action, pending] = useActionState<FormState, FormData>(sendMessageAction, {});
  const key = useIdempotencyKey(state);
  const channels: MessageChannel[] = whatsAppOnly ? ["WhatsApp"] : options.whatsAppAvailable ? ["WhatsApp", "Email"] : ["Email", "WhatsApp"];
  const [channel, setChannel] = useState<MessageChannel>((state.values?.channel as MessageChannel) ?? channels[0]);
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  const left = windowLeft(options.windowClosesAt);
  const blocked = channel === "WhatsApp" ? !options.whatsAppAvailable : !options.emailAvailable;

  return (
    <form action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="idempotencyKey" value={key} />
      <input type="hidden" name="guestId" value={guestId ?? ""} />
      <input type="hidden" name="conversationId" value={conversationId ?? ""} />
      <input type="hidden" name="bookingId" value={bookingId ?? ""} />
      <FormAlert state={state} success={t("sent")} />
      {!whatsAppOnly && (
        <SelectField name="channel" id="message-channel" label={t("channel")} value={channel} onChange={(ev) => setChannel(ev.target.value as MessageChannel)}>
          {channels.map((c) => (
            <option key={c} value={c}>
              {c === "WhatsApp" ? "WhatsApp" : "Email"}
            </option>
          ))}
        </SelectField>
      )}
      {whatsAppOnly && <input type="hidden" name="channel" value="WhatsApp" />}
      {channel === "WhatsApp" && (
        <p className="rounded-xl bg-secondary px-3 py-2 text-xs text-muted-foreground">
          {options.windowOpen ? t("windowOpen", { left: left ?? "" }) : options.whatsAppBlockReason ?? t("windowClosedTemplate")}
        </p>
      )}
      {channel === "Email" && !options.emailAvailable && <p className="rounded-xl bg-secondary px-3 py-2 text-xs text-muted-foreground">{t("noEmail")}</p>}
      {channel === "Email" && <Field name="subject" label={t("subject")} hint={t("subjectHint")} defaultValue={v.subject ?? ""} error={e.subject} maxLength={150} />}
      <div className="grid gap-1.5">
        <Label htmlFor="message-text">{t("text")}</Label>
        <textarea
          id="message-text"
          name="text"
          className={textarea}
          maxLength={2000}
          required
          defaultValue={state.ok ? "" : (v.text ?? "")}
          key={state.ok ? key : "draft"}
          aria-invalid={e.text ? true : undefined}
          aria-describedby={e.text ? "message-text-error" : undefined}
        />
        {e.text && (
          <p id="message-text-error" className="text-sm text-destructive">
            {e.text}
          </p>
        )}
      </div>
      <div>
        <SubmitButton pending={pending} disabled={pending || blocked}>
          {t("send")}
        </SubmitButton>
      </div>
    </form>
  );
}

export function RetryMessageButton({ id }: { id: string }) {
  const t = useTranslations("Messages");
  const [state, action, pending] = useActionState<FormState, FormData>(retryMessageAction, {});
  return (
    <form action={action} className="grid gap-1">
      <input type="hidden" name="id" value={id} />
      <SubmitButton pending={pending} variant="outline" size="sm">
        <RotateCcw aria-hidden />
        {t("retry")}
      </SubmitButton>
      {state.error && <FormAlert state={state} />}
    </form>
  );
}

export function ResolveConversationButton({ id }: { id: string }) {
  const t = useTranslations("Messages");
  const [state, action, pending] = useActionState<FormState, FormData>(resolveConversationAction, {});
  return (
    <form action={action} className="grid gap-2">
      <input type="hidden" name="id" value={id} />
      <FormAlert state={state} success={t("resolved")} />
      <div>
        <SubmitButton pending={pending} variant="outline">
          {t("resolve")}
        </SubmitButton>
      </div>
    </form>
  );
}
