import { Wallet } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { BookingStatusBadge } from "@/components/booking-status-badge";
import { PaymentStatusBadge } from "@/components/finance-badges";
import { DateRangePicker } from "@/components/forms/date-range-picker";
import { NativeSelect } from "@/components/forms/native-select";
import { PageHeader } from "@/components/layout/page-header";
import { EmptyState, ErrorState } from "@/components/states/states";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { hotelTimeZone } from "@/i18n/config";
import { api } from "@/lib/api/server";
import type { DebtList, PaymentPage } from "@/lib/api/types";
import { hotelToday } from "@/lib/bookings/status";
import { isIsoDate } from "@/lib/calendar";
import { paymentMethodLabel, paymentMethods, paymentTypeLabel } from "@/lib/finance/labels";
import { formatHotelLocal, formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";
import { requestNow } from "@/lib/time/request-now";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Finance"))("paymentsTitle") };
}

const scopes = ["current", "upcoming", "overpaid"] as const;
const scopeKey = { current: "debtsCurrent", upcoming: "debtsUpcoming", overpaid: "debtsOverpaid" } as const;
const scopeHint = { current: "debtsCurrentHint", upcoming: "debtsUpcomingHint", overpaid: "debtsOverpaidHint" } as const;

/** Оплаты: «Касса» — платежи за период с итогами по способам; «Долги» — остатки к оплате и переплаты по броням. */
export default async function PaymentsPage({ searchParams }: PageProps<"/admin/payments">) {
  const params = await searchParams;
  const t = await getTranslations("Finance");
  const tab = params.tab === "debts" ? "debts" : "payments";
  const tabs = (
    <nav aria-label={t("paymentsTitle")} className="inline-flex w-fit rounded-full border bg-card p-1 text-sm font-medium">
      {(["payments", "debts"] as const).map((v) => (
        <Link
          key={v}
          href={v === "debts" ? "/admin/payments?tab=debts" : "/admin/payments"}
          aria-current={v === tab ? "page" : undefined}
          className={cn("rounded-full px-4 py-1.5 transition", v === tab ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground")}
        >
          {v === "payments" ? t("tabPayments") : t("tabDebts")}
        </Link>
      ))}
    </nav>
  );

  return (
    <div className="grid gap-6">
      <PageHeader title={t("paymentsTitle")} description={t("paymentsDescription")} />
      {tab === "debts" ? <Debts scopeParam={params.scope} tabs={tabs} /> : <Payments params={params} tabs={tabs} />}
    </div>
  );
}

async function Payments({ params, tabs }: { params: Record<string, string | string[] | undefined>; tabs: React.ReactNode }) {
  const now = await requestNow();
  const t = await getTranslations("Finance");
  const today = hotelToday(hotelTimeZone, now);
  const from = isIsoDate(params.from) ? params.from : today;
  const to = isIsoDate(params.to) ? params.to : from;
  const method = typeof params.method === "string" && (paymentMethods as string[]).includes(params.method) ? params.method : "";
  const page = Math.max(1, Number(params.page) || 1);
  const query = new URLSearchParams({ from, to, page: String(page), pageSize: "50" });
  if (method) query.set("method", method);
  const result = await api<PaymentPage>(`/payments?${query}`);
  const link = (p: number) => {
    const q = new URLSearchParams({ from, to, page: String(p) });
    if (method) q.set("method", method);
    return `/admin/payments?${q}`;
  };

  return (
    <>
      <div className="flex flex-wrap items-end justify-between gap-3">
        {tabs}
        <form method="get" className="flex flex-wrap items-end gap-2">
          <DateRangePicker key={`${from}-${to}`} mode="period" size="compact" startName="from" endName="to" defaultStart={from} defaultEnd={to} today={today} startLabel={t("period")} />
          <NativeSelect name="method" defaultValue={method} aria-label={t("method")} className="h-9 w-auto">
            <option value="">{t("allMethods")}</option>
            {paymentMethods.map((m) => (
              <option key={m} value={m}>
                {paymentMethodLabel[m]}
              </option>
            ))}
          </NativeSelect>
          <Button type="submit" variant="outline">
            {t("show")}
          </Button>
        </form>
      </div>

      {!result.ok ? (
        <ErrorState title={t("loadError")} />
      ) : (
        <>
          <section aria-label={t("cashTitle")} className="grid gap-3 sm:grid-cols-3 xl:grid-cols-[minmax(13rem,1.4fr)_repeat(5,minmax(0,1fr))]">
            <div className="grid min-w-0 gap-1 rounded-2xl border bg-card p-4 shadow-xs sm:col-span-3 xl:col-span-1">
              <span className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t("cashTotal")}</span>
              <span className="tabular font-heading text-2xl">{formatMoney(result.data.totals.total)}</span>
              <span className="text-xs text-muted-foreground">{t("cashCount", { n: result.data.totals.count })}</span>
            </div>
            {paymentMethods.map((m) => (
              <div key={m} className="grid min-w-0 gap-1 rounded-2xl border bg-card p-4 shadow-xs">
                <span className="text-xs text-muted-foreground">{paymentMethodLabel[m]}</span>
                <span className="tabular text-lg font-medium">{formatMoney(result.data.totals.byMethod[m] ?? 0)}</span>
              </div>
            ))}
          </section>
          <p className="-mt-3 text-xs text-muted-foreground">{t("cashHint")}</p>

          {result.data.page.items.length === 0 ? (
            <EmptyState icon={Wallet} title={t("noPayments")} description={t("noPaymentsHint")} />
          ) : (
            <div className="overflow-x-auto rounded-2xl border bg-card">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("colTime")}</TableHead>
                    <TableHead>{t("colBooking")}</TableHead>
                    <TableHead>{t("colType")}</TableHead>
                    <TableHead>{t("method")}</TableHead>
                    <TableHead>{t("colStatus")}</TableHead>
                    <TableHead>{t("colBy")}</TableHead>
                    <TableHead className="text-right">{t("amount")}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {result.data.page.items.map((p) => (
                    <TableRow key={p.id}>
                      <TableCell className="tabular text-sm whitespace-nowrap">
                        <Link href={`/admin/payments/${p.id}`} className="underline-offset-4 hover:underline">
                          {formatHotelLocal(p.paidAt)}
                        </Link>
                      </TableCell>
                      <TableCell className="text-sm">
                        {p.bookingId ? (
                          <Link href={`/admin/bookings/${p.bookingId}`} className="tabular font-medium underline-offset-4 hover:underline">
                            {p.bookingNumber}
                          </Link>
                        ) : (
                          "—"
                        )}
                        {p.guestName && <span className="block text-xs text-muted-foreground">{p.guestName}</span>}
                      </TableCell>
                      <TableCell className="text-sm">{paymentTypeLabel[p.type]}</TableCell>
                      <TableCell className="text-sm">{paymentMethodLabel[p.method]}</TableCell>
                      <TableCell>
                        <PaymentStatusBadge status={p.status} />
                      </TableCell>
                      <TableCell className="text-sm text-muted-foreground">{p.registeredByName ?? "—"}</TableCell>
                      <TableCell className={cn("tabular text-right whitespace-nowrap", p.amount < 0 && "text-destructive")}>{formatMoney(p.amount)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
          {result.data.page.totalPages > 1 && (
            <nav className="flex items-center gap-3 text-sm" aria-label={t("paymentsTitle")}>
              {page > 1 && <Link href={link(page - 1)}>{t("prev")}</Link>}
              <span>{t("pageOf", { page, pages: result.data.page.totalPages })}</span>
              {page < result.data.page.totalPages && <Link href={link(page + 1)}>{t("next")}</Link>}
            </nav>
          )}
        </>
      )}
    </>
  );
}

async function Debts({ scopeParam, tabs }: { scopeParam: string | string[] | undefined; tabs: React.ReactNode }) {
  const t = await getTranslations("Finance");
  const scope = (scopes as readonly string[]).includes(String(scopeParam)) ? (scopeParam as (typeof scopes)[number]) : "current";
  const result = await api<DebtList>(`/finance/debts?scope=${scope}`);
  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        {tabs}
        <nav aria-label={t("tabDebts")} className="flex flex-wrap gap-1.5">
          {scopes.map((s) => (
            <Link
              key={s}
              href={`/admin/payments?tab=debts&scope=${s}`}
              aria-current={s === scope ? "page" : undefined}
              className={cn(
                "rounded-full border px-3 py-1 text-sm font-medium transition",
                s === scope ? "border-primary bg-primary text-primary-foreground" : "border-border bg-card hover:border-primary/40 hover:text-primary",
              )}
            >
              {t(scopeKey[s])}
            </Link>
          ))}
        </nav>
      </div>
      <p className="text-sm text-muted-foreground">{t(scopeHint[scope])}</p>
      {!result.ok ? (
        <ErrorState title={t("loadError")} />
      ) : result.data.items.length === 0 ? (
        <EmptyState icon={Wallet} title={t("noDebts")} />
      ) : (
        <>
          <p className="font-heading text-xl">
            {scope === "overpaid" ? t("overpaidTotal") : t("debtsTotal")}: <span className="tabular">{formatMoney(Math.abs(result.data.total))}</span>
          </p>
          <div className="overflow-x-auto rounded-2xl border bg-card">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("colBooking")}</TableHead>
                  <TableHead>{t("colStay")}</TableHead>
                  <TableHead>{t("colStatus")}</TableHead>
                  <TableHead className="text-right">{t("due")}</TableHead>
                  <TableHead className="text-right">{t("received")}</TableHead>
                  <TableHead className="text-right">{scope === "overpaid" ? t("overpaid") : t("toPay")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {result.data.items.map((d) => (
                  <TableRow key={d.bookingId}>
                    <TableCell>
                      <Link href={`/admin/bookings/${d.bookingId}`} className="tabular font-medium underline-offset-4 hover:underline">
                        {d.number}
                      </Link>
                      <span className="block text-xs text-muted-foreground">
                        {d.guestName}
                        {d.roomNumber ? ` · № ${d.roomNumber}` : ""}
                      </span>
                    </TableCell>
                    <TableCell className="tabular text-sm whitespace-nowrap">{formatHotelLocal(d.startsAt)}</TableCell>
                    <TableCell>
                      <BookingStatusBadge status={d.bookingStatus} />
                    </TableCell>
                    <TableCell className="tabular text-right whitespace-nowrap">{formatMoney(d.due)}</TableCell>
                    <TableCell className="tabular text-right whitespace-nowrap">{formatMoney(d.received)}</TableCell>
                    <TableCell className={cn("tabular text-right font-medium whitespace-nowrap", d.balance > 0 ? "text-destructive" : "text-status-reserved")}>
                      {formatMoney(Math.abs(d.balance))}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </>
      )}
    </>
  );
}
