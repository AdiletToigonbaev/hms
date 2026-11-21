import { Undo2 } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { RefundStatusBadge } from "@/components/finance-badges";
import { PageHeader } from "@/components/layout/page-header";
import { EmptyState, ErrorState } from "@/components/states/states";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { api } from "@/lib/api/server";
import type { RefundPage } from "@/lib/api/types";
import { paymentMethodLabel, refundStatuses, refundStatusLabel } from "@/lib/finance/labels";
import { formatHotelLocal, formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";
import { RefundActions } from "../payments/finance-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Finance"))("refundsTitle") };
}

/** Возвраты: запросы ждут решения (в пределах лимита утверждающего), одобренные — выплаты. */
export default async function RefundsPage({ searchParams }: PageProps<"/admin/refunds">) {
  const params = await searchParams;
  const status = typeof params.status === "string" && (refundStatuses as string[]).includes(params.status) ? params.status : "";
  const page = Math.max(1, Number(params.page) || 1);
  const query = new URLSearchParams({ page: String(page), pageSize: "50" });
  if (status) query.set("status", status);
  const [t, result] = await Promise.all([getTranslations("Finance"), api<RefundPage>(`/refunds?${query}`)]);

  return (
    <div className="grid gap-6">
      <PageHeader title={t("refundsTitle")} description={t("refundsDescription")} />
      <nav aria-label={t("refundsTitle")} className="flex flex-wrap gap-1.5">
        {["", ...refundStatuses].map((s) => (
          <Link
            key={s || "all"}
            href={s ? `/admin/refunds?status=${s}` : "/admin/refunds"}
            aria-current={s === status ? "page" : undefined}
            className={cn(
              "rounded-full border px-3 py-1 text-sm font-medium transition",
              s === status ? "border-primary bg-primary text-primary-foreground" : "border-border bg-card hover:border-primary/40 hover:text-primary",
            )}
          >
            {s ? refundStatusLabel[s as keyof typeof refundStatusLabel] : t("all")}
          </Link>
        ))}
      </nav>

      {!result.ok ? (
        <ErrorState title={t("loadError")} />
      ) : (
        <>
          <p className="text-sm text-muted-foreground">
            {result.data.canApprove
              ? result.data.approveLimit !== null
                ? t("approveLimit", { amount: formatMoney(result.data.approveLimit) })
                : t("approveNoLimit")
              : t("approveNone")}
          </p>
          {result.data.page.items.length === 0 ? (
            <EmptyState icon={Undo2} title={t("noRefunds")} description={t("noRefundsHint")} />
          ) : (
            <div className="overflow-x-auto rounded-2xl border bg-card">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("colRequested")}</TableHead>
                    <TableHead>{t("colBooking")}</TableHead>
                    <TableHead>{t("refundReason")}</TableHead>
                    <TableHead>{t("refundMethod")}</TableHead>
                    <TableHead className="text-right">{t("amount")}</TableHead>
                    <TableHead>{t("colStatus")}</TableHead>
                    {result.data.canApprove && <TableHead>{t("colActions")}</TableHead>}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {result.data.page.items.map((r) => (
                    <TableRow key={r.id} className="align-top">
                      <TableCell className="text-sm whitespace-nowrap">
                        <span className="tabular">{formatHotelLocal(r.requestedAt)}</span>
                        <span className="block text-xs text-muted-foreground">{r.requestedByName}</span>
                      </TableCell>
                      <TableCell className="text-sm">
                        {r.bookingId ? (
                          <Link href={`/admin/bookings/${r.bookingId}`} className="tabular font-medium underline-offset-4 hover:underline">
                            {r.bookingNumber}
                          </Link>
                        ) : (
                          "—"
                        )}
                        <Link href={`/admin/payments/${r.paymentId}`} className="block text-xs text-muted-foreground underline-offset-4 hover:underline">
                          {t("payment")}
                        </Link>
                      </TableCell>
                      <TableCell className="max-w-64 min-w-48 text-sm whitespace-normal">
                        {r.reason}
                        {r.decisionComment && <span className="block text-xs text-muted-foreground">{t("decision", { text: r.decisionComment })}</span>}
                      </TableCell>
                      <TableCell className="text-sm">{paymentMethodLabel[r.method]}</TableCell>
                      <TableCell className="tabular text-right whitespace-nowrap">{formatMoney(r.amount)}</TableCell>
                      <TableCell>
                        <RefundStatusBadge status={r.status} />
                        {r.paidAt && <span className="mt-1 block text-xs text-muted-foreground">{formatHotelLocal(r.paidAt)}</span>}
                      </TableCell>
                      {result.data.canApprove && (
                        <TableCell className="min-w-56">
                          <RefundActions refund={r} />
                        </TableCell>
                      )}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </>
      )}
    </div>
  );
}
