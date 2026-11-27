import { ChevronLeft, ChevronRight } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { DateRangePicker } from "@/components/forms/date-range-picker";
import { PageHeader } from "@/components/layout/page-header";
import { ErrorState } from "@/components/states/states";
import { Button, buttonVariants } from "@/components/ui/button";
import { hotelTimeZone } from "@/i18n/config";
import { api } from "@/lib/api/server";
import type { CalendarData } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { hotelToday } from "@/lib/bookings/status";
import { isIsoDate, shiftDays, weekdayIndex } from "@/lib/calendar";
import { cn } from "@/lib/utils";
import { requestNow } from "@/lib/time/request-now";
import { CalendarBoard } from "./calendar-board";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Calendar"))("title") };
}

/**
 * Календарь занятости. «День» — шкала по часам для всех номеров (основной вид почасовой гостиницы);
 * «Неделя» — 7 дней с понедельника, для посуточных броней. Данные и правила — от API.
 */
export default async function CalendarPage({ searchParams }: PageProps<"/admin/calendar">) {
  const now = await requestNow();
  const params = await searchParams;
  const today = hotelToday(hotelTimeZone, now);
  const view = params.view === "week" ? "week" : "day";
  const date = isIsoDate(params.date) ? params.date : today;
  const from = view === "week" ? shiftDays(date, -weekdayIndex(date)) : date;
  const days = view === "week" ? 7 : 1;
  const to = shiftDays(from, days - 1);

  const [t, user, data] = await Promise.all([
    getTranslations("Calendar"),
    getCurrentUser(),
    api<CalendarData>(`/calendar?from=${from}&to=${to}`),
  ]);
  const can = (p: string) => user?.permissions.includes(p) ?? false;
  const link = (next: { view?: string; date?: string }) => {
    const q = new URLSearchParams({ view: next.view ?? view, date: next.date ?? date });
    if (q.get("view") === "day") q.delete("view");
    if (q.get("date") === today) q.delete("date");
    const s = q.toString();
    return s ? `/admin/calendar?${s}` : "/admin/calendar";
  };

  return (
    <div className="grid gap-5" data-wide>
      <PageHeader title={t("title")} description={t("description")} />

      <div className="flex flex-wrap items-end justify-between gap-3">
        <nav aria-label={t("viewLabel")} className="inline-flex rounded-full border bg-card p-1 text-sm font-medium">
          {(["day", "week"] as const).map((v) => (
            <Link
              key={v}
              href={link({ view: v })}
              aria-current={v === view ? "page" : undefined}
              className={cn("rounded-full px-4 py-1.5 transition", v === view ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground")}
            >
              {v === "day" ? t("viewDay") : t("viewWeek")}
            </Link>
          ))}
        </nav>

        <div className="flex flex-wrap items-end gap-2">
          <Link href={link({ date: shiftDays(date, -days) })} className={buttonVariants({ variant: "outline", size: "icon" })} aria-label={view === "week" ? t("prevWeek") : t("prevDay")}>
            <ChevronLeft aria-hidden />
          </Link>
          <Link href={link({ date: today })} className={buttonVariants({ variant: "outline" })} aria-current={date === today ? "date" : undefined}>
            {t("today")}
          </Link>
          <Link href={link({ date: shiftDays(date, days) })} className={buttonVariants({ variant: "outline", size: "icon" })} aria-label={view === "week" ? t("nextWeek") : t("nextDay")}>
            <ChevronRight aria-hidden />
          </Link>
          <form method="get" className="flex items-end gap-2">
            {view === "week" && <input type="hidden" name="view" value="week" />}
            <DateRangePicker key={date} mode="single" size="compact" startName="date" defaultStart={date} today={today} startLabel={t("pickDate")} />
            <Button type="submit" variant="outline">
              {t("show")}
            </Button>
          </form>
        </div>
      </div>

      {!data.ok ? (
        <ErrorState title={t("loadError")} />
      ) : (
        <CalendarBoard
          key={`${view}-${from}`}
          data={data.data}
          view={view}
          from={from}
          days={days}
          today={today}
          canEdit={can("booking.edit")}
          canCreate={can("booking.create")}
        />
      )}
    </div>
  );
}
