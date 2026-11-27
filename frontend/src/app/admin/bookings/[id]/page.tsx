import { AlarmClock, ArrowLeft, CircleCheck, Receipt } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { BookingStatusBadge } from "@/components/booking-status-badge";
import { PriceBreakdown } from "@/components/price-breakdown";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import { hotelTimeZone } from "@/i18n/config";
import type {
  Booking,
  BookingBalance,
  BookingHistoryItem,
  BookingStatus,
  CalendarData,
  CancellationQuote,
  CheckInPreview,
  CheckOutPreview,
  MoveOptions,
  ExtensionQuote,
} from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { bookingStatusLabel, hotelNowLocal, hotelToday, nightsLabel } from "@/lib/bookings/status";
import { formatHotelDateTime, formatHotelLocal, formatHotelTime, formatMoney } from "@/lib/format";
import { getHotelInfo } from "@/lib/hotel";
import { requestNow } from "@/lib/time/request-now";
import { BookingActions, BookingDetailsForm, ExtendBookingForm, RescheduleForm } from "../booking-forms";
import { CheckInForm, CheckOutForm, MoveGuestForm } from "./front-desk-card";
import { PaymentCard } from "./payment-card";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Bookings"))("title") };
}

function statusOf(values: Record<string, unknown> | null): BookingStatus | null {
  const s = values?.status;
  return typeof s === "string" && s in bookingStatusLabel ? (s as BookingStatus) : null;
}

export default async function BookingPage({ params, searchParams }: PageProps<"/admin/bookings/[id]">) {
  const now = await requestNow();
  const { id } = await params;
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();
  const created = (await searchParams).created === "1";

  const [t, user, booking, history, hotelInfo] = await Promise.all([
    getTranslations("Bookings"),
    getCurrentUser(),
    api<Booking>(`/bookings/${id}`),
    api<BookingHistoryItem[]>(`/bookings/${id}/history`),
    getHotelInfo(),
  ]);
  if (!booking.ok) notFound();
  const b = booking.data;
  const can = (p: string) => user?.permissions.includes(p) ?? false;
  const editable = can("booking.edit") && !["Cancelled", "CheckedOut", "NoShow", "Expired"].includes(b.status);
  const showActions = can("booking.edit") || can("booking.cancel");
  const evKey = (e: string) =>
    ["created", "status_changed", "details_changed", "extended", "rescheduled", "penalty_charged", "penalty_waived", "early_departure", "room_moved"].includes(
      e,
    )
      ? (`ev_${e}` as "ev_created")
      : null;
  const hourly = b.kind === "Hourly";
  const today = hotelToday(hotelTimeZone, now);

  // Условия отмены и штраф за неявку — на сейчас (считает API); номера для переноса — из календаря.
  const needsTerms = b.allowedActions.includes("cancel") || b.allowedActions.includes("noShow");
  const canReschedule = can("booking.edit") && can("calendar.view") && b.allowedActions.includes("reschedule");
  // Стойка: можно ли заселить / что будет при выселении сейчас (считает API).
  const canCheckIn = can("checkin.perform") && b.allowedActions.includes("checkIn");
  const canCheckOut = can("checkout.perform") && b.allowedActions.includes("checkOut");
  const canMove = can("booking.edit") && b.allowedActions.includes("move");
  const [terms, roomsData, balance, checkIn, checkOut, move] = await Promise.all([
    needsTerms && showActions ? api<CancellationQuote>(`/bookings/${id}/cancel/preview`) : null,
    canReschedule ? api<CalendarData>(`/calendar?from=${today}&to=${today}`) : null,
    can("payment.view") ? api<BookingBalance>(`/bookings/${id}/balance`) : null,
    canCheckIn ? api<CheckInPreview>(`/bookings/${id}/check-in/preview`) : null,
    canCheckOut ? api<CheckOutPreview>(`/bookings/${id}/check-out/preview`) : null,
    canMove ? api<MoveOptions>(`/bookings/${id}/move/options`) : null,
  ]);

  // Продление: предел по правилам и по следующей брони номера (считает API).
  let extendMax = 0;
  let extendLimitHint: string | null = null;
  if (can("booking.edit") && b.allowedActions.includes("extend")) {
    const byRules = hourly ? hotelInfo.hourlyMaxHours - (b.hours ?? 0) : Math.min(30, 60 - b.nights);
    const probe = byRules > 0 ? await api<ExtensionQuote>(`/bookings/${id}/extend/quote?units=1`) : null;
    extendMax = probe?.ok ? Math.min(byRules, probe.data.maxUnits ?? byRules) : 0;
    if (probe?.ok && probe.data.nextBookingStartsAt && probe.data.maxUnits !== null && probe.data.maxUnits < byRules) {
      extendLimitHint = t("extendMax", {
        time: formatHotelLocal(probe.data.nextBookingStartsAt),
        n: hourly ? `${probe.data.maxUnits} ч` : nightsLabel(probe.data.maxUnits),
      });
    }
  }

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
            {hourly ? `${formatHotelLocal(b.startsAt)}–${formatHotelTime(b.endsAt)}` : `${formatHotelLocal(b.startsAt)} — ${formatHotelLocal(b.endsAt)}`}
          </span>{" "}
          · {hourly ? `${b.hours} ч · ${t("kindHourly").toLowerCase()}` : nightsLabel(b.nights)} · {t("room")}{" "}
          <Link href={`/admin/rooms/${b.room.roomId}`} className="font-medium text-foreground underline-offset-4 hover:underline">
            {b.room.number}
          </Link>{" "}
          ({b.room.roomTypeName}) · {b.adults} {t("adultsShort")}
          {b.children > 0 ? `, ${b.children} ${t("childrenShort")}` : ""}
        </p>
      </div>

      {(b.actualCheckInAt || b.actualCheckOutAt) && (
        <p className="-mt-3 text-sm text-muted-foreground">
          {b.actualCheckInAt && t("actualCheckIn", { time: formatHotelLocal(b.actualCheckInAt) })}
          {b.actualCheckInAt && b.actualCheckOutAt && " · "}
          {b.actualCheckOutAt && t("actualCheckOut", { time: formatHotelLocal(b.actualCheckOutAt) })}
          {b.earlyDepartureCredit > 0 && ` · ${t("earlyCredit", { amount: formatMoney(b.earlyDepartureCredit) })}`}
        </p>
      )}

      {b.holdUntil && (
        <Alert role="status" className="border-status-cleaning/40 text-status-cleaning">
          <AlarmClock aria-hidden />
          <AlertDescription>{t("holdUntil", { time: formatHotelLocal(b.holdUntil) })}</AlertDescription>
        </Alert>
      )}

      {(b.penaltyAmount > 0 || b.penaltyWaived) && (
        <Alert role="status">
          <Receipt aria-hidden />
          <AlertDescription>
            {b.penaltyWaived
              ? t("penaltyWaivedNote", { kind: b.penaltyKind === "NoShow" ? t("penaltyNoShow") : t("penaltyLateCancel") })
              : t("penaltyNote", { kind: b.penaltyKind === "NoShow" ? t("penaltyNoShow") : t("penaltyLateCancel"), amount: formatMoney(b.penaltyAmount) })}
          </AlertDescription>
        </Alert>
      )}

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
            <CardContent>
              {b.price && (
                <PriceBreakdown
                  quote={b.price}
                  extras={[
                    ...b.extensions.map((x) => ({
                      label: `${t("extensionLine", { n: x.kind === "Hour" ? `${x.quantity} ч` : nightsLabel(x.quantity) })} · до ${formatHotelLocal(x.newEndsAt)}`,
                      amount: x.amount,
                    })),
                    ...(b.roomChangeAmount !== 0 ? [{ label: t("roomChangeLine"), amount: b.roomChangeAmount }] : []),
                  ]}
                  total={b.totalAmount}
                />
              )}
            </CardContent>
          </Card>

          {balance?.ok && <PaymentCard balance={balance.data} startsAt={b.startsAt} now={hotelNowLocal(hotelTimeZone, now)} canRegister={can("payment.register")} canRefund={can("refund.request")} />}

          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{editable ? t("edit") : t("contact")}</h2>
              </CardTitle>
              {b.guestId && can("guest.view") && (
                <CardDescription>
                  <Link href={`/admin/guests/${b.guestId}`} className="font-medium text-primary underline-offset-4 hover:underline">
                    {t("guestCard")} →
                  </Link>
                </CardDescription>
              )}
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
          {(checkIn?.ok || checkOut?.ok) && (
            <Card className="border-primary/30">
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("frontDesk")}</h2>
                </CardTitle>
              </CardHeader>
              <CardContent>
                {checkIn?.ok && <CheckInForm booking={b} preview={checkIn.data} />}
                {checkOut?.ok && <CheckOutForm booking={b} preview={checkOut.data} />}
                {move?.ok && <MoveGuestForm booking={b} options={move.data} />}
              </CardContent>
            </Card>
          )}
          {showActions && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("actions")}</h2>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="grid gap-6">
                  {extendMax > 0 && (
                    <section className="grid gap-2" aria-labelledby="extend-title">
                      <h3 id="extend-title" className="font-heading text-base">
                        {t("extendTitle")}
                      </h3>
                      {extendLimitHint && <p className="text-xs text-muted-foreground">{extendLimitHint}</p>}
                      <ExtendBookingForm booking={b} maxUnits={extendMax} />
                    </section>
                  )}
                  {canReschedule && roomsData?.ok && (
                    <section className="grid gap-2" aria-labelledby="reschedule-title">
                      <h3 id="reschedule-title" className="font-heading text-base">
                        {t("rescheduleTitle")}
                      </h3>
                      <RescheduleForm
                        booking={b}
                        rooms={roomsData.data.rooms}
                        today={today}
                        minHours={roomsData.data.hourlyMinHours}
                        maxHours={roomsData.data.hourlyMaxHours}
                      />
                    </section>
                  )}
                  <BookingActions booking={b} canConfirm={can("booking.edit")} canCancel={can("booking.cancel")} terms={terms?.ok ? terms.data : null} />
                </div>
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
