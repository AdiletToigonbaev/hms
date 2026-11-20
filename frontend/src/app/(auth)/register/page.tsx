import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
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
        <RegisterForm />
      </CardContent>
    </Card>
  );
}
