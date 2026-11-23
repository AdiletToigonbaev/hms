import { Clock3, MoonStar, Search } from "lucide-react";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { DateRangePicker } from "@/components/forms/date-range-picker";
import { NativeSelect } from "@/components/forms/native-select";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { stayQuery, type StaySearch } from "@/lib/site/stay";
import { MAX_NIGHTS } from "@/lib/validation/bookings";
import { cn } from "@/lib/utils";
import { HourlyStartFields } from "./hourly-start-fields";

/**
 * Поиск на сайте: «По часам» (по умолчанию) или «По суткам». Обычная форма GET на /book — работает и без JavaScript,
 * а ссылку с выбором можно отправить другому человеку.
 */
export async function StaySearchForm({
  stay,
  today,
  nowHour,
  minHours,
  maxHours,
  basePath,
  className,
}: {
  stay: StaySearch;
  today: string;
  nowHour: number;
  minHours: number;
  maxHours: number;
  /** Страница, на которой переключается вид брони (сохраняет выбор категории). */
  basePath: string;
  className?: string;
}) {
  const t = await getTranslations("Site");
  const tabs = [
    { hourly: true, label: t("kindHourly"), icon: Clock3 },
    { hourly: false, label: t("kindNightly"), icon: MoonStar },
  ];
  const tall = "h-12 rounded-xl";
  return (
    <div className={cn("grid gap-4 rounded-2xl border bg-card p-4 shadow-sm sm:p-5", className)}>
      <nav aria-label={t("kind")} className="inline-flex w-fit rounded-xl border bg-secondary/60 p-1">
        {tabs.map((tab) => (
          <Link
            key={String(tab.hourly)}
            href={`${basePath}?${stayQuery({ ...stay, hourly: tab.hourly })}`}
            scroll={false}
            aria-current={tab.hourly === stay.hourly ? "page" : undefined}
            className={cn(
              "inline-flex min-h-10 items-center gap-1.5 rounded-lg px-3 text-sm font-medium transition",
              tab.hourly === stay.hourly ? "bg-card text-primary shadow-sm" : "text-muted-foreground hover:text-foreground",
            )}
          >
            <tab.icon className="size-4" aria-hidden />
            {tab.label}
          </Link>
        ))}
      </nav>
      <form
        action="/book"
        method="get"
        className={cn(
          "grid gap-3 sm:grid-cols-2 lg:items-end",
          stay.hourly ? "lg:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)_minmax(0,1fr)_minmax(0,0.8fr)_minmax(0,0.8fr)_auto]" : "lg:grid-cols-[minmax(0,2.5fr)_minmax(0,0.8fr)_minmax(0,0.8fr)_auto]",
        )}
      >
        {stay.roomTypeId && <input type="hidden" name="roomTypeId" value={stay.roomTypeId} />}
        {stay.hourly ? (
          <>
            <input type="hidden" name="kind" value="hourly" />
            <HourlyStartFields key={`${stay.date}-${stay.startHour}`} defaultDate={stay.date} defaultHour={stay.startHour} today={today} nowHour={nowHour} className={tall} />
            <div className="grid gap-1.5">
              <Label htmlFor="hours">{t("hours")}</Label>
              <NativeSelect id="hours" name="hours" defaultValue={String(stay.hours)} className={tall}>
                {Array.from({ length: maxHours - minHours + 1 }, (_, i) => minHours + i).map((h) => (
                  <option key={h} value={h}>
                    {t("hoursN", { n: h })}
                  </option>
                ))}
              </NativeSelect>
            </div>
          </>
        ) : (
          <div className="grid gap-1.5 sm:col-span-2 lg:col-span-1">
            <input type="hidden" name="kind" value="nightly" />
            <span className="text-sm font-medium">{t("dates")}</span>
            <DateRangePicker
              key={`${stay.checkIn}-${stay.checkOut}`}
              mode="stay"
              startName="checkIn"
              endName="checkOut"
              defaultStart={stay.checkIn}
              defaultEnd={stay.checkOut}
              today={today}
              min={today}
              maxNights={MAX_NIGHTS}
              startLabel={t("checkIn")}
              endLabel={t("checkOut")}
            />
          </div>
        )}
        <div className="grid gap-1.5">
          <Label htmlFor="adults">{t("adults")}</Label>
          <NativeSelect id="adults" name="adults" defaultValue={String(stay.adults)} className={tall}>
            {Array.from({ length: 10 }, (_, i) => i + 1).map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </NativeSelect>
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="children">{t("children")}</Label>
          <NativeSelect id="children" name="children" defaultValue={String(stay.children)} className={tall}>
            {Array.from({ length: 11 }, (_, i) => i).map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </NativeSelect>
        </div>
        <Button type="submit" className="h-12 rounded-xl px-6 sm:col-span-2 lg:col-span-1">
          <Search aria-hidden />
          {t("search")}
        </Button>
      </form>
    </div>
  );
}
