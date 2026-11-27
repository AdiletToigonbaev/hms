"use client";

import { Plus, Trash2 } from "lucide-react";
import { useTranslations } from "next-intl";
import { useActionState, useState } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { SelectField } from "@/components/forms/native-select";
import { SubmitButton } from "@/components/forms/submit-button";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import type { AutoReplyItem, MessageTemplate, MessagingSettings } from "@/lib/api/types";
import { templateStatuses, templateStatusLabel } from "@/lib/messaging/labels";
import { saveAutoRepliesAction, saveMessagingSettingsAction, saveTemplateAction, testAutoReplyAction, type AutoReplyTestState } from "./actions";

const textarea =
  "min-h-28 w-full rounded-lg border border-input bg-card px-2.5 py-2 font-mono text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive";

function TextArea({ name, label, defaultValue, error, hint, maxLength, rows }: { name: string; label: string; defaultValue: string; error?: string; hint?: string; maxLength: number; rows?: number }) {
  const describedBy = [hint && `${name}-hint`, error && `${name}-error`].filter(Boolean).join(" ") || undefined;
  return (
    <div className="grid content-start gap-1.5">
      <Label htmlFor={name}>{label}</Label>
      <textarea id={name} name={name} className={textarea} rows={rows} maxLength={maxLength} defaultValue={defaultValue} aria-invalid={error ? true : undefined} aria-describedby={describedBy} />
      {hint && !error && (
        <p id={`${name}-hint`} className="text-xs text-muted-foreground">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${name}-error`} className="text-xs text-destructive">
          {error}
        </p>
      )}
    </div>
  );
}

function Variables({ names }: { names: string[] }) {
  return (
    <p className="flex flex-wrap gap-1 text-xs text-muted-foreground">
      {names.map((n) => (
        <code key={n} className="rounded bg-secondary px-1.5 py-0.5">{`{{${n}}}`}</code>
      ))}
    </p>
  );
}

/** Время напоминаний и тексты автоответчика (приветствие, «не понял», отписка). */
export function MessagingSettingsForm({ settings, variables }: { settings: MessagingSettings; variables: string[] }) {
  const t = useTranslations("Messages");
  const [state, action, pending] = useActionState<FormState, FormData>(saveMessagingSettingsAction, {});
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-5" noValidate>
      <input type="hidden" name="version" value={settings.version} />
      <FormAlert state={state} success={t("settingsSaved")} />
      <fieldset className="grid gap-4">
        <legend className="mb-2 text-base font-medium">{t("reminders")}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field name="hourlyStartReminderMinutes" type="number" min={10} max={1440} label={t("hourlyStart")} hint={t("minutesBefore")} defaultValue={v.hourlyStartReminderMinutes ?? settings.hourlyStartReminderMinutes} error={e.hourlyStartReminderMinutes} />
          <Field name="hourlyEndReminderMinutes" type="number" min={5} max={180} label={t("hourlyEnd")} hint={t("minutesBefore")} defaultValue={v.hourlyEndReminderMinutes ?? settings.hourlyEndReminderMinutes} error={e.hourlyEndReminderMinutes} />
          <Field name="nightlyStartReminderTime" type="time" label={t("nightlyStart")} hint={t("nightlyStartHint")} defaultValue={v.nightlyStartReminderTime ?? settings.nightlyStartReminderTime} error={e.nightlyStartReminderTime} />
          <Field name="nightlyEndReminderMinutes" type="number" min={15} max={720} label={t("nightlyEnd")} hint={t("minutesBefore")} defaultValue={v.nightlyEndReminderMinutes ?? settings.nightlyEndReminderMinutes} error={e.nightlyEndReminderMinutes} />
        </div>
        <p className="text-xs text-muted-foreground">{t("remindersHint")}</p>
      </fieldset>
      <fieldset className="grid gap-4">
        <legend className="mb-2 text-base font-medium">{t("autoReplies")}</legend>
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" name="autoRepliesEnabled" defaultChecked={settings.autoRepliesEnabled} className="size-4 accent-primary" />
          {t("autoRepliesEnabled")}
        </label>
        <TextArea name="greetingText" label={t("greeting")} hint={t("greetingHint")} maxLength={1000} defaultValue={v.greetingText ?? settings.greetingText} error={e.greetingText} />
        <TextArea name="fallbackText" label={t("fallback")} maxLength={1000} defaultValue={v.fallbackText ?? settings.fallbackText} error={e.fallbackText} />
        <TextArea name="stopText" label={t("stop")} hint={t("stopHint")} maxLength={1000} defaultValue={v.stopText ?? settings.stopText} error={e.stopText} />
        <Variables names={variables} />
      </fieldset>
      <div>
        <SubmitButton pending={pending}>{t("save")}</SubmitButton>
      </div>
    </form>
  );
}

/** Шаблон уведомления: письмо, текст WhatsApp (как подан в Meta) и статус модерации. */
export function TemplateForm({ template }: { template: MessageTemplate }) {
  const t = useTranslations("Messages");
  const [state, action, pending] = useActionState<FormState, FormData>(saveTemplateAction, {});
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-5" noValidate>
      <input type="hidden" name="code" value={template.code} />
      <input type="hidden" name="version" value={template.version} />
      <FormAlert state={state} success={t("templateSaved")} />
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" name="isActive" defaultChecked={template.isActive} disabled={!template.event} className="size-4 accent-primary" />
        {template.event ? t("templateActive") : t("templateRequired")}
      </label>
      {!template.event && <input type="hidden" name="isActive" value="on" />}
      <fieldset className="grid gap-4">
        <legend className="mb-2 text-base font-medium">{t("emailPart")}</legend>
        {!template.emailAllowed && <p className="text-xs text-muted-foreground">{t("emailNotUsed")}</p>}
        <Field name="emailSubject" label={t("subject")} maxLength={150} defaultValue={v.emailSubject ?? template.emailSubject} error={e.emailSubject} />
        <TextArea name="emailBody" label={t("emailBody")} rows={8} maxLength={4000} defaultValue={v.emailBody ?? template.emailBody} error={e.emailBody} />
      </fieldset>
      <fieldset className="grid gap-4">
        <legend className="mb-2 text-base font-medium">{t("whatsAppPart")}</legend>
        <p className="text-xs text-muted-foreground">{template.isAuthentication ? t("authTemplateHint") : t("whatsAppTemplateHint")}</p>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field name="whatsAppName" label={t("whatsAppName")} hint={t("whatsAppNameHint")} defaultValue={v.whatsAppName ?? template.whatsAppName ?? ""} error={e.whatsAppName} />
          <SelectField name="whatsAppStatus" label={t("whatsAppStatus")} hint={t("whatsAppStatusHint")} defaultValue={v.whatsAppStatus ?? template.whatsAppStatus} error={e.whatsAppStatus}>
            {templateStatuses.map((s) => (
              <option key={s} value={s}>
                {templateStatusLabel[s]}
              </option>
            ))}
          </SelectField>
        </div>
        <TextArea name="whatsAppBody" label={t("whatsAppBody")} rows={5} maxLength={1024} defaultValue={v.whatsAppBody ?? template.whatsAppBody} error={e.whatsAppBody} />
      </fieldset>
      <div className="grid gap-1">
        <span className="text-xs font-medium">{t("variables")}</span>
        <Variables names={template.variables} />
      </div>
      <div>
        <SubmitButton pending={pending}>{t("save")}</SubmitButton>
      </div>
    </form>
  );
}

interface Row extends AutoReplyItem {
  key: string;
  removed?: boolean;
}

/** Меню автоответчика: цифра, название, ключевые слова, ответ; пункт «администратор» передаёт диалог сотруднику. */
export function AutoRepliesForm({ items, variables }: { items: AutoReplyItem[]; variables: string[] }) {
  const t = useTranslations("Messages");
  const [state, action, pending] = useActionState<FormState, FormData>(saveAutoRepliesAction, {});
  const [rows, setRows] = useState<Row[]>(() => items.map((i) => ({ ...i, key: String(i.number) })));
  const visible = rows.filter((r) => !r.removed);
  const nextNumber = () => [1, 2, 3, 4, 5, 6, 7, 8, 9].find((n) => !visible.some((r) => r.number === n)) ?? 9;
  return (
    <form action={action} className="grid gap-4" noValidate>
      <input type="hidden" name="count" value={rows.length} />
      <FormAlert state={state} success={t("autoRepliesSaved")} />
      {rows.map((r, i) =>
        r.removed ? (
          <input key={r.key} type="hidden" name={`items.${i}.removed`} value="true" />
        ) : (
          <fieldset key={r.key} className="grid gap-3 rounded-2xl border bg-card p-4">
            <legend className="sr-only">{t("menuItem", { number: r.number })}</legend>
            <div className="grid gap-3 sm:grid-cols-[6rem_1fr_12rem]">
              <Field name={`items.${i}.number`} id={`items-${i}-number`} type="number" min={1} max={9} label={t("menuNumber")} defaultValue={r.number} />
              <Field name={`items.${i}.title`} id={`items-${i}-title`} label={t("menuTitle")} maxLength={40} defaultValue={r.title} />
              <SelectField name={`items.${i}.action`} id={`items-${i}-action`} label={t("menuAction")} defaultValue={r.action}>
                <option value="Text">{t("actionText")}</option>
                <option value="Operator">{t("actionOperator")}</option>
              </SelectField>
            </div>
            <Field name={`items.${i}.keywords`} id={`items-${i}-keywords`} label={t("menuKeywords")} hint={t("menuKeywordsHint")} defaultValue={r.keywords.join(", ")} />
            <div className="grid gap-1.5">
              <Label htmlFor={`items-${i}-body`}>{t("menuBody")}</Label>
              <textarea id={`items-${i}-body`} name={`items.${i}.body`} className={textarea} rows={4} maxLength={1500} defaultValue={r.body} />
            </div>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" name={`items.${i}.isActive`} defaultChecked={r.isActive} className="size-4 accent-primary" />
                {t("menuActive")}
              </label>
              <Button type="button" variant="ghost" size="sm" onClick={() => setRows((all) => all.map((x) => (x.key === r.key ? { ...x, removed: true } : x)))} disabled={visible.length <= 1}>
                <Trash2 aria-hidden />
                {t("menuRemove")}
              </Button>
            </div>
          </fieldset>
        ),
      )}
      <div className="grid gap-1">
        <span className="text-xs font-medium">{t("variables")}</span>
        <Variables names={variables} />
      </div>
      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          variant="outline"
          disabled={visible.length >= 9}
          onClick={() => setRows((all) => [...all, { key: crypto.randomUUID(), number: nextNumber(), title: "", keywords: [], body: "", action: "Text", isActive: true }])}
        >
          <Plus aria-hidden />
          {t("menuAdd")}
        </Button>
        <SubmitButton pending={pending}>{t("save")}</SubmitButton>
      </div>
    </form>
  );
}

const outcomes = ["stop", "menu", "operator", "greeting", "fallback", "silent"] as const;

function outcomeKey(outcome: string) {
  if (outcome.startsWith("item")) return "outcome_item" as const;
  const known = outcomes.find((o) => o === outcome) ?? "fallback";
  return `outcome_${known}` as const;
}

/** Проверка ответа автоответчика без WhatsApp: что получит гость на такое сообщение. */
export function AutoReplyTester() {
  const t = useTranslations("Messages");
  const [state, action, pending] = useActionState<AutoReplyTestState, FormData>(testAutoReplyAction, {});
  return (
    <form action={action} className="grid gap-3">
      <Field name="text" label={t("testMessage")} placeholder={t("testPlaceholder")} maxLength={500} />
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" name="firstContact" className="size-4 accent-primary" />
        {t("testFirstContact")}
      </label>
      <div>
        <SubmitButton pending={pending} variant="outline">
          {t("testRun")}
        </SubmitButton>
      </div>
      {state.error && <p className="text-sm text-destructive">{state.error}</p>}
      {state.outcome && (
        <div className="grid gap-1 rounded-2xl border bg-primary/8 p-3 text-sm" role="status">
          <span className="text-xs text-muted-foreground">{t(outcomeKey(state.outcome))}</span>
          <p className="whitespace-pre-line">{state.reply || t("outcomeSilent")}</p>
        </div>
      )}
    </form>
  );
}
