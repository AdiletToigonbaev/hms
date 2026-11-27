import { ChevronRight } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { ErrorState } from "@/components/states/states";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { AutoReplies, MessageTemplate, MessagingSettings, WhatsAppState } from "@/lib/api/types";
import { templateStatusLabel } from "@/lib/messaging/labels";
import { cn } from "@/lib/utils";
import { MessagingSettingsForm } from "./messaging-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Messages"))("ownerTitle") };
}

const stateTone: Record<WhatsAppState["availability"], string> = {
  active: "bg-status-available/12 text-status-available",
  disabled: "bg-secondary text-muted-foreground",
  not_configured: "bg-status-reserved/12 text-status-reserved",
  secrets_missing: "bg-status-reserved/12 text-status-reserved",
};

const stateKey = {
  active: "wa_active",
  disabled: "wa_disabled",
  not_configured: "wa_not_configured",
  secrets_missing: "wa_secrets_missing",
} as const;

/** Сообщения гостям — настройки владельца: время напоминаний, автоответчик, шаблоны уведомлений. */
export default async function OwnerMessagesPage() {
  const [t, errors, settings, templates, autoReplies] = await Promise.all([
    getTranslations("Messages"),
    getTranslations("Errors"),
    api<MessagingSettings>("/messaging/settings"),
    api<MessageTemplate[]>("/messaging/templates"),
    api<AutoReplies>("/messaging/auto-replies"),
  ]);
  if (!settings.ok || !templates.ok) return <ErrorState title={errors("unknown")} />;
  const wa = settings.data.whatsApp;

  return (
    <div className="grid gap-6">
      <PageHeader title={t("ownerTitle")} description={t("ownerDescription")} />

      <div className="flex flex-wrap items-center gap-3 rounded-2xl border bg-card p-4 shadow-xs">
        <span className={cn("rounded-full px-2.5 py-0.5 text-xs font-medium", stateTone[wa.availability])}>{t(stateKey[wa.availability])}</span>
        <p className="min-w-0 flex-1 text-sm text-muted-foreground">{wa.available ? t("waActiveHint", { phone: wa.businessPhone ?? "—" }) : t("waOffHint")}</p>
        <Link href="/owner/integrations" className={buttonVariants({ variant: "outline", size: "sm" })}>
          {t("toIntegrations")}
        </Link>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <Card>
          <CardHeader>
            <CardTitle>
              <h2 className="text-xl">{t("templates")}</h2>
            </CardTitle>
            <CardDescription>{t("templatesHint")}</CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="grid gap-2">
              {templates.data.map((tpl) => (
                <li key={tpl.code}>
                  <Link href={`/owner/messages/templates/${tpl.code}`} className="flex items-center justify-between gap-3 rounded-xl border p-3 transition hover:border-primary/40">
                    <span className="grid gap-0.5">
                      <span className="font-medium">{tpl.title}</span>
                      <span className="flex flex-wrap gap-1.5 text-xs text-muted-foreground">
                        {tpl.emailAllowed && <span>Email ·</span>}
                        <span>WhatsApp: {templateStatusLabel[tpl.whatsAppStatus]}</span>
                      </span>
                    </span>
                    <span className="flex items-center gap-2">
                      {!tpl.isActive && <Badge variant="secondary">{t("off")}</Badge>}
                      <ChevronRight className="size-4 text-muted-foreground" aria-hidden />
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>

        <div className="grid content-start gap-6">
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("settings")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <MessagingSettingsForm settings={settings.data} variables={autoReplies.ok ? autoReplies.data.variables : []} />
            </CardContent>
          </Card>
          <Link href="/owner/messages/auto-replies" className={cn(buttonVariants({ variant: "outline" }), "w-fit")}>
            {t("editMenu")}
          </Link>
        </div>
      </div>
    </div>
  );
}
