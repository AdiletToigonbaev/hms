import { CalendarClock, LogIn, LogOut } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { BookingStatusBadge } from "@/components/booking-status-badge";
import { StatTile } from "@/components/dashboard/stat-tile";
import { PageHeader } from "@/components/layout/page-header";
import { RoomStatusBadge } from "@/components/room-status-badge";
import { ErrorState, LoadingState } from "@/components/states/states";
import { buttonVariants } from "@/components/ui/button";
import { api } from "@/lib/api/server";
import type { OperationsDashboard, RoomStatus, TodayBooking } from "@/lib/api/types";
import { AutoRefresh } from "@/components/auto-refresh";
import { formatHotelTime, formatStayDate } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Dashboard"))("opsTitle") };
}

/** Рабочий стол администратора: что происходит сегодня — заезды, выезды, проживающие, номера, уборка. */
export default function AdminHomePage() {
  return (
    <Suspense fallback={<LoadingState rows={5} />}>
      <Operations />
    </Suspense>
  );
}

async function Operations() {
  const [t, result] = await Promise.all([getTranslations("Dashboard"), api<OperationsDashboard>("/dashboard/operations")]);
  if (!result.ok) return <ErrorState title={t("loadError")} />;
  const d = result.data;
  const pct = new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 0 }).format(d.occupancyToday * 100);
  return (
    <div className="grid gap-6">
      <AutoRefresh seconds={45} />
      <PageHeader
        title={t("opsTitle")}
        description={t("opsText", { date: formatStayDate(d.date) })}
        actions={
          <Link href="/admin/calendar" className={buttonVariants({ variant: "outline" })}>
            <CalendarClock aria-hidden />
            {t("calendar")}
          </Link>
        }
      />
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatTile label={t("arrivals")} value={String(d.arrivals.length)} hint={t("arrivalsHint")} />
        <StatTile label={t("departures")} value={String(d.departures.length)} />
        <StatTile label={t("inHouse")} value={String(d.inHouse)} hint={t("occupancyToday", { pct })} />
        <Link href="/admin/bookings?status=Pending" className="rounded-2xl transition hover:shadow-sm focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none">
          <StatTile label={t("pending")} value={String(d.pendingConfirmations)} hint={t("pendingHint")} tone={d.pendingConfirmations > 0 ? "bad" : undefined} className="h-full" />
        </Link>
      </div>
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <span className="text-muted-foreground">{t("roomsNow")}:</span>
        {Object.entries(d.roomsByStatus).map(([status, n]) => (
          <span key={status} className="inline-flex items-center gap-1.5">
            <RoomStatusBadge status={status as RoomStatus} />
            <span className="tabular font-medium">{n}</span>
          </span>
        ))}
        <Link href="/admin/housekeeping" className="ml-auto text-primary underline-offset-4 hover:underline">
          {t("openCleaning", { n: d.openCleaningTasks })}
        </Link>
      </div>
      <div className="grid gap-6 lg:grid-cols-2">
        <TodayList title={t("arrivals")} icon={<LogIn className="size-4 text-primary" aria-hidden />} items={d.arrivals} time="start" empty={t("noArrivals")} />
        <TodayList title={t("departures")} icon={<LogOut className="size-4 text-primary" aria-hidden />} items={d.departures} time="end" empty={t("noDepartures")} />
      </div>
    </div>
  );
}

function TodayList({ title, icon, items, time, empty }: { title: string; icon: React.ReactNode; items: TodayBooking[]; time: "start" | "end"; empty: string }) {
  return (
    <section aria-label={title} className="grid content-start gap-3 rounded-2xl border bg-card p-4">
      <h2 className="flex items-center gap-2 font-heading text-lg">
        {icon}
        {title}
      </h2>
      {items.length === 0 ? (
        <p className="text-sm text-muted-foreground">{empty}</p>
      ) : (
        <ul className="grid gap-1">
          {items.map((b) => (
            <li key={b.bookingId}>
              <Link href={`/admin/bookings/${b.bookingId}`} className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-lg px-2 py-2 hover:bg-secondary">
                <span className="tabular w-12 font-semibold">{formatHotelTime(time === "start" ? b.startsAt : b.endsAt)}</span>
                <span className="font-medium">№ {b.roomNumber}</span>
                <span className="min-w-0 flex-1 truncate text-sm text-muted-foreground">{b.guestName ?? "—"}</span>
                <BookingStatusBadge status={b.status} />
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
