import { ArrowLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { Card, CardContent } from "@/components/ui/card";
import { GuestForm } from "../guest-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Guests"))("newTitle") };
}

export default async function NewGuestPage() {
  const t = await getTranslations("Guests");
  return (
    <div className="grid max-w-3xl gap-6">
      <div className="grid gap-3">
        <Link href="/admin/guests" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {t("back")}
        </Link>
        <PageHeader title={t("newTitle")} description={t("newDescription")} />
      </div>
      <Card>
        <CardContent>
          <GuestForm />
        </CardContent>
      </Card>
    </div>
  );
}
