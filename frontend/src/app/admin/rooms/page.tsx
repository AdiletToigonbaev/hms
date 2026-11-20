import { BedDouble, Plus } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { NativeSelect } from "@/components/forms/native-select";
import { PageHeader } from "@/components/layout/page-header";
import { RoomStatusBadge } from "@/components/room-status-badge";
import { EmptyState, ErrorState } from "@/components/states/states";
import { Button, buttonVariants } from "@/components/ui/button";
import { api } from "@/lib/api/server";
import type { Room, RoomStatus, RoomType } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { formatMoney } from "@/lib/format";
import { groupByFloor, roomStatusLabel } from "@/lib/rooms/status";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Rooms"))("title") };
}

const statuses: RoomStatus[] = ["Available", "Occupied", "Cleaning", "Maintenance", "OutOfService"];

export default async function RoomsBoardPage({ searchParams }: PageProps<"/admin/rooms">) {
  const params = await searchParams;
  const status = typeof params.status === "string" && (statuses as string[]).includes(params.status) ? params.status : "";
  const typeId = typeof params.type === "string" && /^[0-9a-f-]{36}$/i.test(params.type) ? params.type : "";
  const query = new URLSearchParams();
  if (status) query.set("status", status);
  if (typeId) query.set("roomTypeId", typeId);

  const [t, errors, user, rooms, types] = await Promise.all([
    getTranslations("Rooms"),
    getTranslations("Errors"),
    getCurrentUser(),
    api<Room[]>(`/rooms?${query}`),
    api<RoomType[]>("/room-types"),
  ]);
  const canManage = user?.permissions.includes("room.manage") ?? false;

  if (!rooms.ok || !types.ok) {
    return <ErrorState title={errors("unknown")} />;
  }

  if (types.data.length === 0) {
    return (
      <div className="grid gap-6">
        <PageHeader title={t("title")} description={t("description")} />
        <EmptyState
          icon={BedDouble}
          title={t("noTypes")}
          description={t("noTypesHint")}
          action={
            canManage && (
              <Link href="/admin/room-types/new" className={buttonVariants()}>
                {t("toTypes")}
              </Link>
            )
          }
        />
      </div>
    );
  }

  return (
    <div className="grid gap-6">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          canManage && (
            <Link href="/admin/rooms/new" className={buttonVariants()}>
              <Plus aria-hidden />
              {t("add")}
            </Link>
          )
        }
      />

      <form method="get" className="flex flex-wrap items-center gap-2" role="search">
        <NativeSelect name="status" defaultValue={status} aria-label={t("status")} className="w-auto">
          <option value="">{t("allStatuses")}</option>
          {statuses.map((s) => (
            <option key={s} value={s}>
              {roomStatusLabel[s]}
            </option>
          ))}
        </NativeSelect>
        <NativeSelect name="type" defaultValue={typeId} aria-label={t("type")} className="w-auto">
          <option value="">{t("allTypes")}</option>
          {types.data.map((type) => (
            <option key={type.id} value={type.id}>
              {type.name}
            </option>
          ))}
        </NativeSelect>
        <Button type="submit" variant="outline">
          {t("filter")}
        </Button>
      </form>

      {rooms.data.length === 0 ? (
        <EmptyState icon={BedDouble} title={t("empty")} description={t("emptyHint")} />
      ) : (
        groupByFloor(rooms.data).map(([floor, list]) => (
          <section key={floor ?? "none"} className="grid gap-3" aria-label={floor === null ? t("noFloor") : `${t("floor")} ${floor}`}>
            <h2 className="text-lg text-muted-foreground">{floor === null ? t("noFloor") : `${t("floor")} ${floor}`}</h2>
            <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
              {list.map((room) => (
                <li key={room.id}>
                  <Link
                    href={`/admin/rooms/${room.id}`}
                    className="grid h-full gap-2 rounded-2xl border bg-card p-4 shadow-xs transition hover:-translate-y-0.5 hover:shadow-md focus-visible:ring-3 focus-visible:ring-ring/50"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <span className="font-heading text-2xl leading-none">{room.number}</span>
                      <RoomStatusBadge status={room.displayStatus} />
                    </div>
                    <span className="truncate text-sm text-muted-foreground">{room.roomTypeName}</span>
                    <span className="tabular text-sm">
                      {formatMoney(room.effectivePrice)} <span className="text-muted-foreground">{t("perNight")}</span>
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        ))
      )}
    </div>
  );
}
