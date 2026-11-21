import { AlarmClock, ArrowLeft, CircleCheck } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { BookingStatusBadge } from "@/components/booking-status-badge";
import { PaymentStatusBadge } from "@/components/finance-badges";
import { PriceBreakdown } from "@/components/price-breakdown";
import { LoadingState } from "@/components/states/states";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { CancellationQuote, ExtensionQuote, MyBooking, MyBookingBalance } from "@/lib/api/types";
import { nightsLabel } from "@/lib/bookings/status";
import { paymentMethodLabel, paymentTypeLabel, refundStatusLabel } from "@/lib/finance/labels";
import { formatHotelLocal, formatHotelTime, formatMoney } from "@/lib/format";
import { getHotelInfo } from "@/lib/hotel";
import { CancelMyBookingForm, ExtendMyBookingForm, RefundRequestForm } from "../../account-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Account"))("bookingTitle") };
}

const uuid = /^[0-9a-f-]{36}$/i;

export default function MyBookingPage({ params, searchParams }: PageProps<"/account/bookings/[id]">) {
  return (
    <Suspense fallback={<LoadingState />}>
      <MyBookingContent params={params} searchParams={searchParams} />
    </Suspense>
  );
}

async function MyBookingContent({ params, searchParams }: PageProps<"/account/bookings/[id]">) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  if (!uuid.test(id)) notFound();
  const [t, booking, balance, hotel] = await Promise.all([
    getTranslations("Account"),
    api<MyBooking>(`/me/bookings/${id}`),
    api<MyBookingBalance>(`/me/bookings/${id}/balance`),
    getHotelInfo(),
  ]);
  // Чужая или несуществующая бронь — API отвечает 404, страница — «не найдено».
  if (!booking.ok) notFound();
  const b = booking.data;
  const hourly = b.kind === "Hourly";
  const canCancel = b.allowedActions.includes("cancel");
  const canExtend = b.allowedActions.includes("extend");
  const [terms, probe] = await Promise.all([
    canCancel ? api<CancellationQuote>(`/me/bookings/${id}/cancel/preview`) : null,
    canExtend ? api<ExtensionQuote>(`/me/bookings/${id}/extend/quote?units=1`) : null,
  ]);
  // Продление: в пределах правил гостиницы и до следующей брони номера (считает API).
  const byRules = hourly ? hotel.hourlyMaxHours - (b.hours ?? 0) : 30;
  const extendMax = probe?.ok ? Math.min(byRules, probe.data.maxUnits ?? byRules) : 0;
  const bal = balance.ok ? balance.data : null;

  return (
    <div className="grid gap-6">
      <div className="grid gap-2">
        <Link href="/account" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {t("back")}
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="font-heading text-3xl">{b.roomTypeName}</h1>
          <BookingStatusBadge status={b.status} />
        </div>
        <p className="tabular text-muted-foreground">
          {hourly ? `${formatHotelLocal(b.startsAt)}–${formatHotelTime(b.endsAt)} · ${b.hours} ч` : `${formatHotelLocal(b.startsAt)} — ${formatHotelLocal(b.endsAt)} · ${nightsLabel(b.nights)}`}
          {" · "}
          {t("guestsN", { n: b.adults + b.children })}
          {b.roomNumber && ` · ${t("roomN", { n: b.roomNumber })}`}
        </p>
        <p className="text-sm text-muted-foreground">{t("numberN", { number: b.number })}</p>
      </div>

      {query.created === "1" && (
        <Alert role="status">
          <CircleCheck aria-hidden />
          <AlertDescription>{b.status === "Pending" ? t("createdPending") : t("createdConfirmed")}</AlertDescription>
        </Alert>
      )}
      {b.status === "Pending" && b.holdUntil && (
        <Alert role="status" className="border-status-cleaning/40 text-status-cleaning">
          <AlarmClock aria-hidden />
          <AlertDescription>{t("holdUntil", { time: formatHotelLocal(b.holdUntil) })}</AlertDescription>
        </Alert>
      )}
      {b.penaltyAmount > 0 && (
        <Alert role="status">
          <AlertDescription>{t("penalty", { amount: formatMoney(b.penaltyAmount) })}</AlertDescription>
        </Alert>
      )}

      <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <div className="grid content-start gap-6">
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("price")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              {b.price && (
                <PriceBreakdown
                  quote={b.price}
                  extras={[
                    ...b.extensions.map((x) => ({
                      label: `${t("extensionLine", { n: x.kind === "Hour" ? `${x.quantity} ч` : nightsLabel(x.quantity) })} · ${formatHotelLocal(x.newEndsAt)}`,
                      amount: x.amount,
                    })),
                    ...(b.earlyDepartureCredit > 0 ? [{ label: t("earlyCredit"), amount: -b.earlyDepartureCredit }] : []),
                  ]}
                  total={b.totalAmount}
                />
              )}
              <p className="mt-3 text-xs text-muted-foreground">{t("payAtHotel")}</p>
            </CardContent>
          </Card>

          {bal && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("payments")}</h2>
                </CardTitle>
              </CardHeader>
              <CardContent className="grid gap-4">
                <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
                  <dt className="text-muted-foreground">{t("due")}</dt>
                  <dd className="tabular text-right">{formatMoney(bal.due)}</dd>
                  <dt className="text-muted-foreground">{t("received")}</dt>
                  <dd className="tabular text-right">{formatMoney(bal.received - bal.refunded)}</dd>
                  <dt className="font-medium">{bal.balance >= 0 ? t("toPay") : t("overpaid")}</dt>
                  <dd className={`tabular text-right font-semibold ${bal.balance > 0 ? "text-destructive" : ""}`}>{formatMoney(Math.abs(bal.balance))}</dd>
                </dl>
                {bal.prepaymentRequired > 0 && bal.received < bal.prepaymentRequired && (
                  <p className="text-sm text-muted-foreground">{t("prepayment", { amount: formatMoney(bal.prepaymentRequired) })}</p>
                )}
                {bal.payments.length > 0 && (
                  <ul className="grid gap-2 border-t pt-3">
                    {bal.payments.map((p) => (
                      <li key={p.id} className="flex flex-wrap items-center justify-between gap-2 text-sm">
                        <span>
                          {paymentTypeLabel[p.type]} · {paymentMethodLabel[p.method]}
                          <span className="block text-xs text-muted-foreground tabular">{formatHotelLocal(p.paidAt)}</span>
                        </span>
                        <span className="flex items-center gap-2">
                          <PaymentStatusBadge status={p.status} />
                          <span className="tabular font-medium">{formatMoney(p.amount)}</span>
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
                {bal.refunds.length > 0 && (
                  <ul className="grid gap-2 border-t pt-3">
                    {bal.refunds.map((r) => (
                      <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 text-sm">
                        <span>
                          {t("refund")} · {refundStatusLabel[r.status]}
                          {r.decisionComment && <span className="block text-xs text-muted-foreground">{r.decisionComment}</span>}
                        </span>
                        <span className="tabular font-medium">{formatMoney(r.amount)}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </CardContent>
            </Card>
          )}
        </div>

        <div className="grid content-start gap-6">
          {(canExtend && extendMax > 0) || canCancel || (bal && bal.refundable > 0) ? (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("actions")}</h2>
                </CardTitle>
              </CardHeader>
              <CardContent className="grid gap-6">
                {canExtend && extendMax > 0 && <ExtendMyBookingForm booking={b} maxUnits={extendMax} />}
                {bal && bal.refundable > 0 && <RefundRequestForm bookingId={b.id} refundable={bal.refundable} />}
                {canCancel && <CancelMyBookingForm booking={b} terms={terms?.ok ? terms.data : null} />}
              </CardContent>
            </Card>
          ) : null}
          {b.cancelReason && (
            <p className="text-sm text-muted-foreground">
              {t("cancelledBecause")}: {b.cancelReason}
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
