import { ArrowLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { AmenitiesForm, ArchiveTypeForm, PhotoManager, RoomTypeForm } from "@/app/admin/rooms/room-forms";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { Amenity, RoomType } from "@/lib/api/types";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("RoomTypes"))("title") };
}

export default async function RoomTypePage({ params }: PageProps<"/admin/room-types/[id]">) {
  const { id } = await params;
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();

  const [t, rooms, type, amenities] = await Promise.all([
    getTranslations("RoomTypes"),
    getTranslations("Rooms"),
    api<RoomType>(`/room-types/${id}`),
    api<Amenity[]>("/amenities"),
  ]);
  if (!type.ok) notFound();
  const rt = type.data;

  return (
    <div className="grid gap-6">
      <div className="grid gap-3">
        <Link href="/admin/room-types" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {t("title")}
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-3xl">{rt.name}</h1>
          {rt.isArchived && <Badge variant="secondary">{t("archived")}</Badge>}
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <div className="grid content-start gap-6">
          <Card>
            <CardContent>
              <RoomTypeForm type={rt} />
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("amenities")}</h2>
              </CardTitle>
              <CardDescription>{t("amenitiesHint")}</CardDescription>
            </CardHeader>
            <CardContent>
              {amenities.ok && <AmenitiesForm typeId={rt.id} all={amenities.data} selected={rt.amenities.map((a) => a.id)} />}
            </CardContent>
          </Card>
        </div>
        <div className="grid content-start gap-6">
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{rooms("photos")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <PhotoManager owner="type" ownerId={rt.id} photos={rt.photos} sortable />
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{rt.isArchived ? t("restore") : t("archive")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <ArchiveTypeForm type={rt} />
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
