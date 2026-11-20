import { CalendarRange, Plus } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { BookingStatusBadge } from "@/components/booking-status-badge";
import { DateRangePicker } from "@/components/forms/date-range-picker";
import { NativeSelect } from "@/components/forms/native-select";
import { PageHeader } from "@/components/layout/page-header";
import { EmptyState, ErrorState } from "@/components/states/states";
import { Button, buttonVariants } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { api } from "@/lib/api/server";
import type { BookingListItem, Paged } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { hotelTimeZone } from "@/i18n/config";
import { addDays, bookingStatusLabel, bookingStatuses, hotelToday, nightsLabel } from "@/lib/bookings/status";
import { cn } from "@/lib/utils";
import { formatHotelTime, formatMoney, formatStayDate } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Bookings"))("title") };
}

const isoDate = /^\d{4}-\d{2}-\d{2}$/;

export default async function BookingsPage({ searchParams }: PageProps<"/admin/bookings">) {
  const params = await searchParams;
  const search = typeof params.search === "string" ? params.search.slice(0, 100) : "";
  const status = typeof params.status === "string" && (bookingStatuses as string[]).includes(params.status) ? params.status : "";
  const today = hotelToday(hotelTimeZone);
  // Без параметров — брони на сегодня: в почасовой гостинице это основной рабочий вид.
  const defaultToday = Object.keys(params).length === 0;
  const from = typeof params.from === "string" && isoDate.test(params.from) ? params.from : defaultToday ? today : "";
  const to = typeof params.to === "string" && isoDate.test(params.to) ? params.to : defaultToday ? today : "";
  const kind = params.kind === "Hourly" || params.kind === "Nightly" ? params.kind : "";
  const page = Math.max(1, Number(params.page) || 1);

  const query = new URLSearchParams({ page: String(page), pageSize: "25" });
  if (search) query.set("search", search);
  if (status) query.set("status", status);
  if (from) query.set("from", from);
  if (to) query.set("to", to);
  if (kind) query.set("kind", kind);
  const filtered = Boolean(search || status || from || to || kind);
  const tomorrow = addDays(today, 1);
  const presets = [
    { label: "presetToday" as const, href: `/admin/bookings?from=${today}&to=${today}`, active: from === today && to === today },
    { label: "presetTomorrow" as const, href: `/admin/bookings?from=${tomorrow}&to=${tomorrow}`, active: from === tomorrow && to === tomorrow },
    { label: "presetAll" as const, href: "/admin/bookings?all=1", active: !from && !to },
  ];

  const [t, errors, user, result] = await Promise.all([
    getTranslations("Bookings"),
    getTranslations("Errors"),
    getCurrentUser(),
    api<Paged<BookingListItem>>(`/bookings?${query}`),
  ]);
  const canCreate = user?.permissions.includes("booking.create") ?? false;

  const pageLink = (p: number) => {
    const next = new URLSearchParams(query);
    next.set("page", String(p));
    next.delete("pageSize");
    return `/admin/bookings?${next}`;
  };

  return (
    <div className="grid gap-6">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          canCreate && (
            <Link href="/admin/bookings/new" className={buttonVariants()}>
              <Plus aria-hidden />
              {t("add")}
            </Link>
          )
        }
      />

      <nav aria-label={t("period")} className="flex flex-wrap gap-1.5">
        {presets.map((p) => (
          <Link
            key={p.label}
            href={p.href}
            aria-current={p.active ? "page" : undefined}
            className={cn(
              "rounded-full border px-3 py-1 text-sm font-medium transition",
              p.active ? "border-primary bg-primary text-primary-foreground" : "border-border bg-card hover:border-primary/40 hover:text-primary",
            )}
          >
            {t(p.label)}
          </Link>
        ))}
      </nav>

      <form method="get" className="flex flex-wrap items-end gap-2" role="search">
        <input type="hidden" name="all" value="1" />
        <Input name="search" defaultValue={search} placeholder={t("searchPlaceholder")} aria-label={t("search")} className="w-full sm:max-w-xs" />
        <NativeSelect name="status" defaultValue={status} aria-label={t("colStatus")} className="w-auto">
          <option value="">{t("allStatuses")}</option>
          {bookingStatuses.map((s) => (
            <option key={s} value={s}>
              {bookingStatusLabel[s]}
            </option>
          ))}
        </NativeSelect>
        <NativeSelect name="kind" defaultValue={kind} aria-label={t("kind")} className="w-auto">
          <option value="">{t("allKinds")}</option>
          <option value="Hourly">{t("kindHourly")}</option>
          <option value="Nightly">{t("kindNightly")}</option>
        </NativeSelect>
        <DateRangePicker
          key={`${from}-${to}`}
          mode="period"
          size="compact"
          startName="from"
          endName="to"
          defaultStart={from || null}
          defaultEnd={to || null}
          today={today}
          startLabel={t("from")}
          endLabel={t("to")}
          className="w-full sm:w-60"
        />
        <Button type="submit" variant="outline">
          {t("filter")}
        </Button>
      </form>

      {!result.ok ? (
        <ErrorState title={errors("unknown")} />
      ) : result.data.items.length === 0 ? (
        filtered ? (
          <EmptyState icon={CalendarRange} title={t("notFound")} description={t("notFoundHint")} />
        ) : (
          <EmptyState
            icon={CalendarRange}
            title={t("empty")}
            description={t("emptyHint")}
            action={
              canCreate && (
                <Link href="/admin/bookings/new" className={buttonVariants()}>
                  {t("add")}
                </Link>
              )
            }
          />
        )
      ) : (
        <>
          <div className="overflow-x-auto rounded-2xl border bg-card">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("colNumber")}</TableHead>
                  <TableHead>{t("colGuest")}</TableHead>
                  <TableHead>{t("colDates")}</TableHead>
                  <TableHead>{t("colRoom")}</TableHead>
                  <TableHead>{t("colStatus")}</TableHead>
                  <TableHead className="text-right">{t("colTotal")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {result.data.items.map((b) => (
                  <TableRow key={b.id}>
                    <TableCell>
                      <Link href={`/admin/bookings/${b.id}`} className="tabular font-medium underline-offset-4 hover:underline">
                        {b.number}
                      </Link>
                    </TableCell>
                    <TableCell>
                      <div className="grid">
                        <span>{b.guestName}</span>
                        {b.guestPhone && <span className="tabular text-xs text-muted-foreground">{b.guestPhone}</span>}
                      </div>
                    </TableCell>
                    <TableCell className="tabular whitespace-nowrap">
                      {b.kind === "Hourly" ? (
                        <>
                          {formatStayDate(b.checkIn)}, {formatHotelTime(b.startsAt)}–{formatHotelTime(b.endsAt)}
                          <span className="block text-xs text-muted-foreground">
                            {t("hoursShort", { n: b.hours ?? 0 })}
                          </span>
                        </>
                      ) : (
                        <>
                          {formatStayDate(b.checkIn)} — {formatStayDate(b.checkOut)}
                          <span className="block text-xs text-muted-foreground">{nightsLabel(b.nights)}</span>
                        </>
                      )}
                    </TableCell>
                    <TableCell>
                      <span className="font-medium">{b.roomNumber}</span>
                      <span className="block text-xs text-muted-foreground">{b.roomTypeName}</span>
                    </TableCell>
                    <TableCell>
                      <BookingStatusBadge status={b.status} />
                    </TableCell>
                    <TableCell className="tabular text-right whitespace-nowrap">{formatMoney(b.totalAmount)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          {result.data.totalPages > 1 && (
            <nav className="flex items-center gap-3 text-sm" aria-label={t("title")}>
              {page > 1 && <Link href={pageLink(page - 1)}>{t("prev")}</Link>}
              <span>{t("pageOf", { page, pages: result.data.totalPages })}</span>
              {page < result.data.totalPages && <Link href={pageLink(page + 1)}>{t("next")}</Link>}
            </nav>
          )}
        </>
      )}
    </div>
  );
}
