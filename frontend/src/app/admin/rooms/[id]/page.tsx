import { ArrowLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { RoomStatusBadge } from "@/components/room-status-badge";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { Room, RoomStatusHistoryItem, RoomType } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { formatHotelDateTime, formatMoney } from "@/lib/format";
import { roomStatusLabel } from "@/lib/rooms/status";
import { PhotoManager, RoomDangerZone, RoomForm, StatusChangeForm } from "../room-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Rooms"))("title") };
}

export default async function RoomPage({ params }: PageProps<"/admin/rooms/[id]">) {
  const { id } = await params;
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();

  const [t, user, room, history, types] = await Promise.all([
    getTranslations("Rooms"),
    getCurrentUser(),
    api<Room>(`/rooms/${id}`),
    api<RoomStatusHistoryItem[]>(`/rooms/${id}/status-history`),
    api<RoomType[]>("/room-types"),
  ]);
  if (!room.ok) notFound();
  const r = room.data;
  const can = (p: string) => user?.permissions.includes(p) ?? false;

  return (
    <div className="grid gap-6">
      <div className="grid gap-3">
        <Link href="/admin/rooms" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {t("title")}
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-3xl">
            {t("number")} {r.number}
          </h1>
          <RoomStatusBadge status={r.displayStatus} />
          {r.isArchived && <Badge variant="outline">{t("archived")}</Badge>}
        </div>
        <p className="text-sm text-muted-foreground">
          {r.roomTypeName} · {r.floor !== null ? `${t("floor")} ${r.floor} · ` : ""}
          <span className="tabular">{formatMoney(r.effectivePrice)}</span> {t("perNight")} · {r.effectiveCapacity} {t("guests")}
        </p>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <div className="grid content-start gap-6">
          {can("room.status.change") && !r.isArchived && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("changeStatus")}</h2>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <StatusChangeForm room={r} />
              </CardContent>
            </Card>
          )}
          {can("room.manage") && types.ok && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("save")}</h2>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <RoomForm room={r} types={types.data.filter((type) => !type.isArchived || type.id === r.roomTypeId)} />
              </CardContent>
            </Card>
          )}
          {can("room.manage") && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("photos")}</h2>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <PhotoManager owner="room" ownerId={r.id} photos={r.photos} />
              </CardContent>
            </Card>
          )}
        </div>

        <div className="grid content-start gap-6">
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("history")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              {!history.ok || history.data.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t("historyEmpty")}</p>
              ) : (
                <ol className="grid gap-4">
                  {history.data.map((h) => (
                    <li key={h.id} className="grid gap-1 border-l-2 border-primary/30 pl-3">
                      <p className="text-sm font-medium">
                        {roomStatusLabel[h.fromStatus]} → {roomStatusLabel[h.toStatus]}
                      </p>
                      {h.reason && <p className="text-sm">{h.reason}</p>}
                      <p className="text-xs text-muted-foreground">
                        {formatHotelDateTime(h.changedAt)} · {h.changedByName ?? t("bySystem")} · {t(`source_${h.source}`)}
                      </p>
                    </li>
                  ))}
                </ol>
              )}
            </CardContent>
          </Card>
          {can("room.manage") && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{r.isArchived ? t("restore") : t("archive")}</h2>
                </CardTitle>
                <CardDescription>{t("deleteHint")}</CardDescription>
              </CardHeader>
              <CardContent>
                <RoomDangerZone room={r} />
              </CardContent>
            </Card>
          )}
        </div>
      </div>
    </div>
  );
}
