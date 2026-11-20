import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ResetPasswordForm } from "./reset-form";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Auth"))("resetTitle") };
}

export default async function ResetPasswordPage() {
  const t = await getTranslations("Auth");
  return (
    <Card>
      <CardHeader>
        <CardTitle className="font-heading text-2xl">
          <h1>{t("resetTitle")}</h1>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <Suspense>
          <ResetPasswordForm />
        </Suspense>
      </CardContent>
    </Card>
  );
}
