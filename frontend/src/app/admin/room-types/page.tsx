import { BedDouble, Plus, Users } from "lucide-react";
import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { EmptyState, ErrorState } from "@/components/states/states";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import { mediaUrl } from "@/lib/api/media";
import { api } from "@/lib/api/server";
import type { RoomType } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { formatMoney } from "@/lib/format";
import { bedTypeLabel } from "@/lib/rooms/status";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("RoomTypes"))("title") };
}

export default async function RoomTypesPage() {
  const [t, errors, user, types] = await Promise.all([
    getTranslations("RoomTypes"),
    getTranslations("Errors"),
    getCurrentUser(),
    api<RoomType[]>("/room-types?includeArchived=true"),
  ]);
  const canManage = user?.permissions.includes("room.manage") ?? false;
  const add = canManage && (
    <Link href="/admin/room-types/new" className={buttonVariants()}>
      <Plus aria-hidden />
      {t("add")}
    </Link>
  );

  return (
    <div className="grid gap-6">
      <PageHeader title={t("title")} description={t("description")} actions={add} />
      {!types.ok ? (
        <ErrorState title={errors("unknown")} />
      ) : types.data.length === 0 ? (
        <EmptyState icon={BedDouble} title={t("empty")} description={t("emptyHint")} action={add} />
      ) : (
        <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {types.data.map((type) => (
            <li key={type.id}>
              <Link
                href={`/admin/room-types/${type.id}`}
                className="group grid h-full overflow-hidden rounded-2xl border bg-card shadow-xs transition hover:-translate-y-0.5 hover:shadow-md"
              >
                <div className="relative aspect-[16/10] bg-secondary">
                  {type.photos[0] ? (
                    <Image src={mediaUrl(type.photos[0].fileId)} alt="" fill unoptimized className="object-cover" />
                  ) : (
                    <BedDouble className="absolute inset-0 m-auto size-10 text-muted-foreground/50" aria-hidden />
                  )}
                  {type.isArchived && (
                    <Badge variant="secondary" className="absolute top-2 left-2">
                      {t("archived")}
                    </Badge>
                  )}
                </div>
                <div className="grid gap-2 p-4">
                  <h2 className="text-xl">{type.name}</h2>
                  <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
                    <Users className="size-4" aria-hidden />
                    {type.capacity} · {bedTypeLabel[type.bedType]} · {type.activeRoomCount} {t("rooms")}
                  </p>
                  <p className="tabular">
                    <span className="text-muted-foreground">{t("from")} </span>
                    <span className="font-semibold">{formatMoney(type.basePrice)}</span>
                    <span className="text-sm text-muted-foreground"> {t("perNight")}</span>
                    {type.hourlyPrice !== null && (
                      <>
                        <span className="text-muted-foreground"> · </span>
                        <span className="font-semibold">{formatMoney(type.hourlyPrice)}</span>
                        <span className="text-sm text-muted-foreground"> {t("perHour")}</span>
                      </>
                    )}
                  </p>
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
