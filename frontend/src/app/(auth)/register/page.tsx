import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { ConfirmationChannels } from "@/lib/api/types";
import { RegisterForm } from "./register-form";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Auth"))("registerTitle") };
}

export default async function RegisterPage() {
  const t = await getTranslations("Auth");
  return (
    <Card>
      <CardHeader>
        <CardTitle className="font-heading text-2xl">
          <h1>{t("registerTitle")}</h1>
        </CardTitle>
        <CardDescription>{t("registerDescription")}</CardDescription>
      </CardHeader>
      <CardContent>
        <Suspense>
          <RegisterFormWithChannels />
        </Suspense>
      </CardContent>
    </Card>
  );
}

/** Код в WhatsApp предлагается, только когда WhatsApp подключён и шаблон кода одобрен (иначе — письмо). */
async function RegisterFormWithChannels() {
  const channels = await api<ConfirmationChannels>("/auth/confirmation-channels", { anonymous: true });
  return <RegisterForm whatsAppAvailable={channels.ok && channels.data.whatsApp} />;
}
