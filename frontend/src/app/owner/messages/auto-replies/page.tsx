import { ArrowLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ErrorState } from "@/components/states/states";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { AutoReplies } from "@/lib/api/types";
import { AutoRepliesForm, AutoReplyTester } from "../messaging-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Messages"))("menuTitlePage") };
}

/** Меню автоответчика WhatsApp (без ИИ — решение заказчика 2026-11-24) и проверка ответа. */
export default async function AutoRepliesPage() {
  const [t, errors, data] = await Promise.all([getTranslations("Messages"), getTranslations("Errors"), api<AutoReplies>("/messaging/auto-replies")]);
  if (!data.ok) return <ErrorState title={errors("unknown")} />;
  return (
    <div className="grid gap-6">
      <div className="grid gap-2">
        <Link href="/owner/messages" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {t("backToSettings")}
        </Link>
        <h1 className="text-3xl">{t("menuTitlePage")}</h1>
        <p className="max-w-3xl text-sm text-muted-foreground">{t("menuDescription")}</p>
      </div>
      <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <AutoRepliesForm items={data.data.items} variables={data.data.variables} />
        <Card className="h-fit">
          <CardHeader>
            <CardTitle>
              <h2 className="text-xl">{t("testTitle")}</h2>
            </CardTitle>
            <CardDescription>{t("testHint")}</CardDescription>
          </CardHeader>
          <CardContent>
            <AutoReplyTester />
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
