import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ForgotPasswordForm } from "./forgot-form";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Auth"))("forgotTitle") };
}

export default async function ForgotPasswordPage() {
  const t = await getTranslations("Auth");
  return (
    <Card>
      <CardHeader>
        <CardTitle className="font-heading text-2xl">
          <h1>{t("forgotTitle")}</h1>
        </CardTitle>
        <CardDescription>{t("forgotDescription")}</CardDescription>
      </CardHeader>
      <CardContent>
        <ForgotPasswordForm />
      </CardContent>
    </Card>
  );
}
