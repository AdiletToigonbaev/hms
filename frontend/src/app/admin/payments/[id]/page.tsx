import { ArrowLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { PaymentStatusBadge, RefundStatusBadge } from "@/components/finance-badges";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { PaymentDetails } from "@/lib/api/types";
import { paymentMethodLabel, paymentStatusLabel, paymentTypeLabel } from "@/lib/finance/labels";
import { formatHotelLocal, formatMoney } from "@/lib/format";
import { RefundRequestForm, ReversePaymentForm } from "../finance-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Finance"))("paymentTitle") };
}

const kindKey = { Charge: "txCharge", Refund: "txRefund", Reversal: "txReversal", StatusChange: "txStatus" } as const;

export default async function PaymentPage({ params, searchParams }: PageProps<"/admin/payments/[id]">) {
  const { id } = await params;
  const { refund: suggestedRaw } = await searchParams;
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();
  const [t, result] = await Promise.all([getTranslations("Finance"), api<PaymentDetails>(`/payments/${id}`)]);
  if (!result.ok) notFound();
  const { payment: p, transactions, refunds, allowedActions } = result.data;
  const reserved = refunds.filter((r) => r.status !== "Rejected").reduce((s, r) => s + r.amount, 0);
  // Переход с брони «Вернуть переплату»: сумма подставляется, но не больше доступного.
  const suggested = Number(typeof suggestedRaw === "string" ? suggestedRaw : NaN);
  const refundAmount = Number.isFinite(suggested) && suggested > 0 ? Math.min(suggested, p.amount - reserved) : undefined;

  return (
    <div className="grid max-w-5xl gap-6">
      <div className="grid gap-3">
        <Link
          href={p.bookingId ? `/admin/bookings/${p.bookingId}` : "/admin/payments"}
          className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="size-4" aria-hidden />
          {p.bookingNumber ? t("backToBooking", { number: p.bookingNumber }) : t("backToPayments")}
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="tabular text-3xl">{formatMoney(p.amount)}</h1>
          <PaymentStatusBadge status={p.status} />
        </div>
        <p className="text-sm text-muted-foreground">
          {paymentTypeLabel[p.type]} · {paymentMethodLabel[p.method]} · {formatHotelLocal(p.paidAt)}
          {p.registeredByName ? ` · ${p.registeredByName}` : ""}
          {p.guestName ? ` · ${p.guestName}` : ""}
        </p>
        {p.comment && <p className="text-sm">{p.comment}</p>}
        {p.reversalOfPaymentId && (
          <Link href={`/admin/payments/${p.reversalOfPaymentId}`} className="w-fit text-sm font-medium text-primary underline-offset-4 hover:underline">
            {t("reversalOf")} →
          </Link>
        )}
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <div className="grid content-start gap-6">
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("journal")}</h2>
              </CardTitle>
              <CardDescription>{t("journalHint")}</CardDescription>
            </CardHeader>
            <CardContent>
              <ol className="grid gap-3">
                {transactions.map((x) => (
                  <li key={x.id} className="grid gap-0.5 border-l-2 border-primary/30 pl-3 text-sm">
                    <span className="flex flex-wrap justify-between gap-2">
                      <span className="font-medium">
                        {t(kindKey[x.kind])} · {paymentStatusLabel[x.status]}
                      </span>
                      {x.amount !== 0 && <span className="tabular">{formatMoney(x.amount)}</span>}
                    </span>
                    {x.comment && <span>{x.comment}</span>}
                    <span className="text-xs text-muted-foreground">
                      {formatHotelLocal(x.occurredAt)} · {x.byName ?? t("bySystem")}
                    </span>
                  </li>
                ))}
              </ol>
            </CardContent>
          </Card>

          {refunds.length > 0 && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("refunds")}</h2>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <ul className="grid gap-2">
                  {refunds.map((r) => (
                    <li key={r.id} className="grid gap-1 rounded-xl border px-3 py-2 text-sm">
                      <span className="flex flex-wrap items-center justify-between gap-2">
                        <span className="tabular font-medium">{formatMoney(r.amount)}</span>
                        <RefundStatusBadge status={r.status} />
                      </span>
                      <span>{r.reason}</span>
                      <span className="text-xs text-muted-foreground">
                        {t("requestedBy", { name: r.requestedByName ?? "—", at: formatHotelLocal(r.requestedAt) })}
                        {r.decidedAt ? ` · ${t("decidedBy", { name: r.decidedByName ?? "—", at: formatHotelLocal(r.decidedAt) })}` : ""}
                        {r.paidAt ? ` · ${t("paidOutBy", { name: r.paidByName ?? "—", at: formatHotelLocal(r.paidAt) })}` : ""}
                      </span>
                    </li>
                  ))}
                </ul>
                <Link href="/admin/refunds" className="mt-3 inline-block text-sm font-medium text-primary underline-offset-4 hover:underline">
                  {t("allRefunds")} →
                </Link>
              </CardContent>
            </Card>
          )}
        </div>

        <div className="grid content-start gap-6">
          {allowedActions.includes("refund") && (
            <Card id="refund" className="scroll-mt-20">
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("refundTitle")}</h2>
                </CardTitle>
                <CardDescription>{t("refundHint")}</CardDescription>
              </CardHeader>
              <CardContent>
                <RefundRequestForm payment={p} available={p.amount - reserved} suggested={refundAmount} />
              </CardContent>
            </Card>
          )}
          {allowedActions.includes("reverse") && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("reverseTitle")}</h2>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <ReversePaymentForm payment={p} />
              </CardContent>
            </Card>
          )}
        </div>
      </div>
    </div>
  );
}
