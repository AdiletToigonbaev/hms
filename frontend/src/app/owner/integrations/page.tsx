import { CreditCard, Mail, MessageCircle } from "lucide-react";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { IntegrationForm } from "@/app/owner/settings/settings-forms";
import { PageHeader } from "@/components/layout/page-header";
import { ErrorState } from "@/components/states/states";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { Integration, IntegrationAvailability } from "@/lib/api/types";
import { formatHotelDateTime } from "@/lib/format";
import { siteUrl } from "@/lib/site/url";
import { cn } from "@/lib/utils";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Integrations"))("title") };
}

const tone: Record<IntegrationAvailability, string> = {
  awaiting_access: "bg-secondary text-muted-foreground",
  secrets_missing: "bg-status-reserved/12 text-status-reserved",
  ready: "bg-status-occupied/12 text-status-occupied",
  active: "bg-status-available/12 text-status-available",
};

const icon = { MBank: CreditCard, Optima: CreditCard, WhatsApp: MessageCircle, Email: Mail } as const;

export default async function IntegrationsPage() {
  const [t, errors, integrations] = await Promise.all([
    getTranslations("Integrations"),
    getTranslations("Errors"),
    api<Integration[]>("/settings/integrations"),
  ]);
  return (
    <div className="grid gap-6">
      <PageHeader title={t("title")} description={t("description")} />
      {!integrations.ok ? (
        <ErrorState title={errors("unknown")} />
      ) : (
        <div className="grid gap-4 lg:grid-cols-2">
          {integrations.data.map((i) => {
            const Icon = icon[i.provider];
            return (
              <Card key={i.provider}>
                <CardHeader>
                  <div className="flex items-start justify-between gap-3">
                    <CardTitle className="flex items-center gap-2.5">
                      <span className="grid size-9 place-items-center rounded-xl bg-secondary">
                        <Icon className="size-5" aria-hidden />
                      </span>
                      <h2 className="text-lg">{i.displayName}</h2>
                    </CardTitle>
                    <span className={cn("rounded-full px-2.5 py-0.5 text-xs font-medium", tone[i.availability])}>{t(i.availability)}</span>
                  </div>
                  {i.availability === "awaiting_access" && <CardDescription>{t("awaitingHint")}</CardDescription>}
                  {i.provider === "WhatsApp" && i.availability !== "awaiting_access" && (
                    <CardDescription>{t("webhookHint", { url: `${siteUrl().origin}/api/v1/webhooks/whatsapp` })}</CardDescription>
                  )}
                  {i.lastCheckAt && (
                    <CardDescription>
                      {t("lastCheck")}: {formatHotelDateTime(i.lastCheckAt)}
                      {i.provider === "WhatsApp" && i.lastCheckStatus ? ` — ${i.lastCheckStatus === "ok" ? t("checkOk") : i.lastCheckStatus.replace(/^error: /, "")}` : ""}
                    </CardDescription>
                  )}
                </CardHeader>
                <CardContent>
                  <IntegrationForm integration={i} />
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
