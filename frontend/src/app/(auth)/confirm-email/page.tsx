import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ConfirmEmailForm, ResendConfirmationForm } from "./confirm-form";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Auth"))("confirmTitle") };
}

export default async function ConfirmEmailPage() {
  const t = await getTranslations("Auth");
  return (
    <div className="grid gap-4">
      <Card>
        <CardHeader>
          <CardTitle>
            <h1>{t("confirmTitle")}</h1>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <Suspense>
            <ConfirmEmailForm />
          </Suspense>
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>
            <h2>{t("resendTitle")}</h2>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <ResendConfirmationForm />
        </CardContent>
      </Card>
    </div>
  );
}
