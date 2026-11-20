import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { ErrorState } from "@/components/states/states";
import { Card, CardContent } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { RoomType } from "@/lib/api/types";
import { RoomForm } from "../room-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Rooms"))("newTitle") };
}

export default async function NewRoomPage() {
  const [t, errors, types] = await Promise.all([getTranslations("Rooms"), getTranslations("Errors"), api<RoomType[]>("/room-types")]);
  return (
    <div className="grid max-w-3xl gap-6">
      <PageHeader title={t("newTitle")} />
      {types.ok ? (
        <Card>
          <CardContent>
            <RoomForm types={types.data} />
          </CardContent>
        </Card>
      ) : (
        <ErrorState title={errors("unknown")} />
      )}
    </div>
  );
}
