import { CalendarPlus, ChevronRight } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { BookingStatusBadge } from "@/components/booking-status-badge";
import { PageHeader } from "@/components/layout/page-header";
import { EmptyState, ErrorState, LoadingState } from "@/components/states/states";
import { buttonVariants } from "@/components/ui/button";
import { api } from "@/lib/api/server";
import type { MyBooking } from "@/lib/api/types";
import { nightsLabel } from "@/lib/bookings/status";
import { formatHotelLocal, formatHotelTime, formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Account"))("title") };
}

/** Мои брони: предстоящие и текущие, отдельно — история. */
export default function AccountPage({ searchParams }: PageProps<"/account">) {
  return (
    <Suspense fallback={<LoadingState />}>
      <MyBookings searchParams={searchParams} />
    </Suspense>
  );
}

async function MyBookings({ searchParams }: Pick<PageProps<"/account">, "searchParams">) {
  const params = await searchParams;
  const history = params.scope === "history";
  const [t, result] = await Promise.all([getTranslations("Account"), api<MyBooking[]>(`/me/bookings?scope=${history ? "history" : "current"}`)]);
  return (
    <div className="grid gap-6">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          <Link href="/#rooms" className={buttonVariants({ className: "h-11" })}>
            <CalendarPlus aria-hidden />
            {t("bookNew")}
          </Link>
        }
      />
      <nav aria-label={t("title")} className="flex gap-1.5">
        {[
          { href: "/account", label: t("current"), active: !history },
          { href: "/account?scope=history", label: t("history"), active: history },
        ].map((tab) => (
          <Link
            key={tab.href}
            href={tab.href}
            aria-current={tab.active ? "page" : undefined}
            className={cn(
              "rounded-full border px-4 py-2 text-sm font-medium transition",
              tab.active ? "border-primary bg-primary text-primary-foreground" : "border-border bg-card hover:border-primary/40 hover:text-primary",
            )}
          >
            {tab.label}
          </Link>
        ))}
      </nav>
      {!result.ok ? (
        <ErrorState title={t("loadError")} />
      ) : result.data.length === 0 ? (
        <EmptyState icon={CalendarPlus} title={history ? t("historyEmpty") : t("currentEmpty")} description={history ? undefined : t("currentEmptyHint")} />
      ) : (
        <ul className="grid gap-3">
          {result.data.map((b) => (
            <li key={b.id}>
              <Link href={`/account/bookings/${b.id}`} className="flex items-center gap-4 rounded-2xl border bg-card p-4 transition hover:border-primary/40 hover:shadow-sm">
                <div className="grid min-w-0 flex-1 gap-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-heading text-lg">{b.roomTypeName}</span>
                    <BookingStatusBadge status={b.status} />
                  </div>
                  <p className="tabular text-sm text-muted-foreground">
                    {b.kind === "Hourly" ? `${formatHotelLocal(b.startsAt)}–${formatHotelTime(b.endsAt)} · ${b.hours} ч` : `${formatHotelLocal(b.startsAt)} — ${formatHotelLocal(b.endsAt)} · ${nightsLabel(b.nights)}`}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {t("numberN", { number: b.number })} · <span className="tabular">{formatMoney(b.status === "Cancelled" || b.status === "NoShow" ? b.penaltyAmount : b.totalAmount)}</span>
                  </p>
                </div>
                <ChevronRight className="size-5 shrink-0 text-muted-foreground" aria-hidden />
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
