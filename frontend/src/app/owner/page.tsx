import { FileBarChart } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { OwnerCharts } from "@/components/dashboard/owner-charts";
import { PeriodFilter, readHour } from "@/components/dashboard/period-filter";
import { StatTile } from "@/components/dashboard/stat-tile";
import { PageHeader } from "@/components/layout/page-header";
import { ErrorState, LoadingState } from "@/components/states/states";
import { buttonVariants } from "@/components/ui/button";
import { hotelTimeZone } from "@/i18n/config";
import { api } from "@/lib/api/server";
import type { OwnerDashboard, RoomType } from "@/lib/api/types";
import { hotelToday } from "@/lib/bookings/status";
import { formatMoney } from "@/lib/format";
import { requestNow } from "@/lib/time/request-now";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Dashboard"))("ownerTitle") };
}

const str = (v: string | string[] | undefined) => (typeof v === "string" ? v : undefined);
const iso = /^\d{4}-\d{2}-\d{2}$/;
const uuid = /^[0-9a-f-]{36}$/i;
const pct = (v: number) => `${new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 1 }).format(v * 100)} %`;
const num = (v: number) => new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 2 }).format(v);

/** Аналитическая панель владельца (ТЗ §12): деньги, номера, брони за период; графики по дням и разрезы. */
export default function OwnerDashboardPage({ searchParams }: PageProps<"/owner">) {
  return (
    <Suspense fallback={<LoadingState rows={6} />}>
      <Dashboard searchParams={searchParams} />
    </Suspense>
  );
}

async function Dashboard({ searchParams }: Pick<PageProps<"/owner">, "searchParams">) {
  const now = await requestNow();
  const params = await searchParams;
  const today = hotelToday(hotelTimeZone, now);
  const period = ["day", "week", "month", "year"].includes(str(params.period) ?? "") ? str(params.period)! : null;
  const from = iso.test(str(params.from) ?? "") ? str(params.from)! : null;
  const to = iso.test(str(params.to) ?? "") ? str(params.to)! : null;
  const roomTypeId = uuid.test(str(params.roomTypeId) ?? "") ? str(params.roomTypeId)! : null;
  const query = new URLSearchParams();
  if (period) query.set("period", period);
  else if (from || to) {
    if (from) query.set("from", from);
    if (to) query.set("to", to);
  } else query.set("period", "month");
  if (roomTypeId) query.set("roomTypeId", roomTypeId);
  const fromHour = readHour(params.fromHour);
  const toHour = readHour(params.toHour);
  if (fromHour !== null) query.set("fromHour", String(fromHour));
  if (toHour !== null) query.set("toHour", String(toHour));

  const [t, result, types] = await Promise.all([getTranslations("Dashboard"), api<OwnerDashboard>(`/dashboard/owner?${query}`), api<RoomType[]>("/room-types")]);
  const roomTypes = types.ok ? types.data.filter((x) => !x.isArchived).map((x) => ({ id: x.id, name: x.name })) : [];

  return (
    <div className="grid gap-6">
      <PageHeader
        title={t("ownerTitle")}
        description={t("ownerText")}
        actions={
          <Link href={`/admin/reports?${query}`} className={buttonVariants({ variant: "outline" })}>
            <FileBarChart aria-hidden />
            {t("reports")}
          </Link>
        }
      />
      <PeriodFilter
        basePath="/owner"
        activePeriod={period ?? (from || to ? null : "month")}
        from={result.ok ? result.data.from : (from ?? today)}
        to={result.ok ? result.data.to : (to ?? today)}
        today={today}
        roomTypes={roomTypes}
        roomTypeId={roomTypeId}
        fromHour={fromHour}
        toHour={toHour}
      />
      {!result.ok ? (
        <ErrorState title={t("loadError")} description={result.problem.title} />
      ) : (
        <Content data={result.data} filtered={roomTypeId !== null} />
      )}
    </div>
  );
}

async function Content({ data, filtered }: { data: OwnerDashboard; filtered: boolean }) {
  const t = await getTranslations("Dashboard");
  const k = data.kpi;
  return (
    <>
      <section aria-labelledby="money-title" className="grid gap-3">
        <h2 id="money-title" className="font-heading text-xl">
          {t("money")}
        </h2>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatTile label={t("accrued")} value={formatMoney(k.accruedRevenue)} hint={t("accruedHint")} />
          <StatTile label={t("cashIn")} value={formatMoney(k.cashIn)} hint={t("cashInHint")} />
          <StatTile label={t("netRevenue")} value={formatMoney(k.netRevenue)} hint={t("netRevenueHint", { refunds: formatMoney(k.refundsPaid) })} />
          <StatTile label={t("expenses")} value={formatMoney(k.expenses)} hint={filtered ? t("expensesFiltered") : undefined} />
          <StatTile label={t("netProfit")} value={formatMoney(k.netProfit)} hint={t("netProfitHint")} tone={k.netProfit < 0 ? "bad" : undefined} />
          <StatTile label={t("accruedProfit")} value={formatMoney(k.accruedProfit)} hint={t("accruedProfitHint")} tone={k.accruedProfit < 0 ? "bad" : undefined} />
          <StatTile label={t("outstanding")} value={formatMoney(data.outstanding)} hint={t("outstandingHint", { n: data.outstandingBookings })} tone={data.outstanding > 0 ? "bad" : undefined} />
          <StatTile label={t("refundsPending")} value={formatMoney(data.refundsRequested)} hint={t("refundsPendingHint", { n: data.refundsRequestedCount })} />
        </div>
      </section>

      <section aria-labelledby="rooms-title" className="grid gap-3">
        <h2 id="rooms-title" className="font-heading text-xl">
          {t("rooms")}
        </h2>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatTile label={t("occupancy")} value={pct(k.occupancy)} hint={t("occupancyHint", { n: data.availableRooms })} />
          <StatTile label={t("avgHourPrice")} value={k.avgHourPrice === null ? "—" : formatMoney(k.avgHourPrice)} hint={t("avgHourPriceHint", { hours: num(k.hoursSold) })} />
          <StatTile label={t("revenuePerRoomHour")} value={formatMoney(k.revenuePerRoomHour)} hint={t("revenuePerRoomHourHint")} />
          <StatTile label={t("turnover")} value={num(k.turnover)} hint={t("turnoverHint")} />
          <StatTile label={t("adr")} value={k.adr === null ? "—" : formatMoney(k.adr)} hint={t("adrHint", { nights: num(k.roomNightsSold) })} />
          <StatTile label={t("revPar")} value={formatMoney(k.revPar)} hint={t("revParHint")} />
        </div>
      </section>

      <section aria-labelledby="bookings-title" className="grid gap-3">
        <h2 id="bookings-title" className="font-heading text-xl">
          {t("bookings")}
        </h2>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatTile label={t("totalBookings")} value={num(k.totalBookings)} hint={t("totalBookingsHint", { hourly: data.bookingsByKind.Hourly ?? 0, nightly: data.bookingsByKind.Nightly ?? 0 })} />
          <StatTile label={t("cancelled")} value={num(k.cancelledBookings)} hint={t("cancelledHint", { rate: pct(k.cancellationRate), noShow: k.noShowBookings })} />
          <StatTile label={t("avgHours")} value={k.avgStayHours === null ? "—" : t("hoursN", { n: num(k.avgStayHours) })} hint={t("avgHoursHint")} />
          <StatTile label={t("avgNights")} value={k.avgStayNights === null ? "—" : num(k.avgStayNights)} hint={t("avgNightsHint")} />
        </div>
      </section>

      <OwnerCharts data={data} />
    </>
  );
}
