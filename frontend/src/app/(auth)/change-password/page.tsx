import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ChangePasswordForm } from "./change-form";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Auth"))("changeTitle") };
}

/** Доступ проверяет proxy (раздел /change-password требует входа), смену пароля — API. */
export default async function ChangePasswordPage() {
  const t = await getTranslations("Auth");
  return (
    <Card>
      <CardHeader>
        <CardTitle className="font-heading text-2xl">
          <h1>{t("changeTitle")}</h1>
        </CardTitle>
        <CardDescription>{t("changeRequired")}</CardDescription>
      </CardHeader>
      <CardContent>
        <ChangePasswordForm />
      </CardContent>
    </Card>
  );
}
