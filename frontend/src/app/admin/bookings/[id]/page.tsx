import { ArrowLeft, CircleCheck } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { BookingStatusBadge } from "@/components/booking-status-badge";
import { PriceBreakdown } from "@/components/price-breakdown";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { Booking, BookingHistoryItem, BookingStatus } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { bookingStatusLabel, nightsLabel } from "@/lib/bookings/status";
import { formatHotelDateTime, formatStayDate } from "@/lib/format";
import { BookingActions, BookingDetailsForm } from "../booking-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Bookings"))("title") };
}

function statusOf(values: Record<string, unknown> | null): BookingStatus | null {
  const s = values?.status;
  return typeof s === "string" && s in bookingStatusLabel ? (s as BookingStatus) : null;
}

export default async function BookingPage({ params, searchParams }: PageProps<"/admin/bookings/[id]">) {
  const { id } = await params;
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();
  const created = (await searchParams).created === "1";

  const [t, user, booking, history] = await Promise.all([
    getTranslations("Bookings"),
    getCurrentUser(),
    api<Booking>(`/bookings/${id}`),
    api<BookingHistoryItem[]>(`/bookings/${id}/history`),
  ]);
  if (!booking.ok) notFound();
  const b = booking.data;
  const can = (p: string) => user?.permissions.includes(p) ?? false;
  const editable = can("booking.edit") && !["Cancelled", "CheckedOut", "NoShow", "Expired"].includes(b.status);
  const showActions = can("booking.edit") || can("booking.cancel");
  const evKey = (e: string) => (["created", "status_changed", "details_changed"].includes(e) ? (`ev_${e}` as "ev_created") : null);

  return (
    <div className="grid gap-6">
      <div className="grid gap-3">
        <Link href="/admin/bookings" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {t("back")}
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="tabular text-3xl">
            {t("colNumber")} {b.number}
          </h1>
          <BookingStatusBadge status={b.status} />
        </div>
        <p className="text-sm text-muted-foreground">
          <span className="tabular">
            {formatStayDate(b.checkIn)} — {formatStayDate(b.checkOut)}
          </span>{" "}
          · {nightsLabel(b.nights)} · {t("room")}{" "}
          <Link href={`/admin/rooms/${b.room.roomId}`} className="font-medium text-foreground underline-offset-4 hover:underline">
            {b.room.number}
          </Link>{" "}
          ({b.room.roomTypeName}) · {b.adults} {t("adultsShort")}
          {b.children > 0 ? `, ${b.children} ${t("childrenShort")}` : ""}
        </p>
      </div>

      {created && (
        <Alert role="status">
          <CircleCheck aria-hidden />
          <AlertDescription>{t("created")}</AlertDescription>
        </Alert>
      )}

      <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <div className="grid content-start gap-6">
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("price")}</h2>
              </CardTitle>
              <CardDescription>{t("priceNote")}</CardDescription>
            </CardHeader>
            <CardContent>{b.price && <PriceBreakdown quote={b.price} />}</CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{editable ? t("edit") : t("contact")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              {editable ? (
                <BookingDetailsForm booking={b} />
              ) : (
                <dl className="grid gap-1 text-sm">
                  <dt className="text-muted-foreground">{t("colGuest")}</dt>
                  <dd>{b.guestName}</dd>
                  <dd className="tabular">{[b.guestPhone, b.guestEmail].filter(Boolean).join(" · ")}</dd>
                  {b.comment && <dd className="mt-2 whitespace-pre-line">{b.comment}</dd>}
                </dl>
              )}
            </CardContent>
          </Card>
        </div>

        <div className="grid content-start gap-6">
          {showActions && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("actions")}</h2>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <BookingActions booking={b} />
                {b.cancelReason && (
                  <p className="mt-2 text-sm">
                    <span className="text-muted-foreground">{t("cancelReason")}:</span> {b.cancelReason}
                  </p>
                )}
              </CardContent>
            </Card>
          )}

          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("history")}</h2>
              </CardTitle>
              <CardDescription>
                {b.source === "Web" ? t("sourceWeb") : t("sourceAdmin")} · {formatHotelDateTime(b.createdAt)}
              </CardDescription>
            </CardHeader>
            <CardContent>
              {!history.ok || history.data.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t("historyEmpty")}</p>
              ) : (
                <ol className="grid gap-4">
                  {history.data.map((h) => {
                    const key = evKey(h.eventType);
                    const from = statusOf(h.oldValues);
                    const to = statusOf(h.newValues);
                    return (
                      <li key={h.id} className="grid gap-1 border-l-2 border-primary/30 pl-3">
                        <p className="text-sm font-medium">
                          {key ? t(key) : h.eventType}
                          {h.eventType === "status_changed" && from && to && `: ${bookingStatusLabel[from]} → ${bookingStatusLabel[to]}`}
                        </p>
                        {h.comment && <p className="text-sm">{h.comment}</p>}
                        <p className="text-xs text-muted-foreground">
                          {formatHotelDateTime(h.changedAt)} · {h.changedByName ?? t("bySystem")}
                        </p>
                      </li>
                    );
                  })}
                </ol>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
