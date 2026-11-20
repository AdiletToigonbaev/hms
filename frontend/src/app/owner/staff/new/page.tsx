import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { CreateStaffForm } from "./create-form";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Staff"))("createTitle") };
}

export default async function NewStaffPage() {
  const t = await getTranslations("Staff");
  return (
    <Card className="max-w-xl">
      <CardHeader>
        <CardTitle>
          <h1>{t("createTitle")}</h1>
        </CardTitle>
        <CardDescription>{t("createDescription")}</CardDescription>
      </CardHeader>
      <CardContent>
        <CreateStaffForm />
      </CardContent>
    </Card>
  );
}
