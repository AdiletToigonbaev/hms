import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { RoomTypeForm } from "@/app/admin/rooms/room-forms";
import { PageHeader } from "@/components/layout/page-header";
import { Card, CardContent } from "@/components/ui/card";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("RoomTypes"))("newTitle") };
}

export default async function NewRoomTypePage() {
  const t = await getTranslations("RoomTypes");
  return (
    <div className="grid max-w-3xl gap-6">
      <PageHeader title={t("newTitle")} description={t("description")} />
      <Card>
        <CardContent>
          <RoomTypeForm />
        </CardContent>
      </Card>
    </div>
  );
}
