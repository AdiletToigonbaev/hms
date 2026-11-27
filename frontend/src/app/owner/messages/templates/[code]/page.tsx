import { ArrowLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { MessageTemplate, TemplatePreview } from "@/lib/api/types";
import { formatHotelDateTime } from "@/lib/format";
import { TemplateForm } from "../../messaging-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Messages"))("templates") };
}

/** Шаблон уведомления: тексты письма и WhatsApp, статус модерации Meta, предпросмотр на примере. */
export default async function TemplatePage({ params }: PageProps<"/owner/messages/templates/[code]">) {
  const { code } = await params;
  if (!/^[a-z_]{3,50}$/.test(code)) notFound();
  const [t, templates, preview] = await Promise.all([
    getTranslations("Messages"),
    api<MessageTemplate[]>("/messaging/templates"),
    api<TemplatePreview>(`/messaging/templates/${code}/ru/preview`),
  ]);
  const template = templates.ok ? templates.data.find((x) => x.code === code) : undefined;
  if (!template) notFound();

  return (
    <div className="grid gap-6">
      <div className="grid gap-2">
        <Link href="/owner/messages" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {t("backToSettings")}
        </Link>
        <h1 className="text-3xl">{template.title}</h1>
        <p className="text-sm text-muted-foreground">{t("updatedAt", { date: formatHotelDateTime(template.updatedAt) })}</p>
      </div>
      <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <Card>
          <CardContent className="pt-6">
            <TemplateForm template={template} />
          </CardContent>
        </Card>
        {preview.ok && (
          <Card className="h-fit">
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("preview")}</h2>
              </CardTitle>
              <CardDescription>{t("previewHint")}</CardDescription>
            </CardHeader>
            <CardContent className="grid gap-4 text-sm">
              {template.emailAllowed && (
                <div className="grid gap-1 rounded-xl border p-3">
                  <span className="text-xs text-muted-foreground">Email</span>
                  <p className="font-medium">{preview.data.subject}</p>
                  <p className="whitespace-pre-line">{preview.data.emailBody}</p>
                </div>
              )}
              <div className="grid gap-1 rounded-xl border bg-primary/8 p-3">
                <span className="text-xs text-muted-foreground">WhatsApp</span>
                <p className="whitespace-pre-line">{preview.data.whatsAppBody}</p>
              </div>
            </CardContent>
          </Card>
        )}
      </div>
    </div>
  );
}
