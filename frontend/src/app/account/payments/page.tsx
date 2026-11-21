import { Wallet } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { PaymentStatusBadge, RefundStatusBadge } from "@/components/finance-badges";
import { PageHeader } from "@/components/layout/page-header";
import { EmptyState, ErrorState, LoadingState } from "@/components/states/states";
import { api } from "@/lib/api/server";
import type { MyPayment, MyRefund } from "@/lib/api/types";
import { paymentMethodLabel, paymentTypeLabel } from "@/lib/finance/labels";
import { formatHotelLocal, formatMoney } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Account"))("paymentsTitle") };
}

/** История платежей и возвратов гостя — только по своим броням. */
export default function MyPaymentsPage() {
  return (
    <Suspense fallback={<LoadingState />}>
      <MyPayments />
    </Suspense>
  );
}

async function MyPayments() {
  const [t, payments, refunds] = await Promise.all([getTranslations("Account"), api<MyPayment[]>("/me/payments"), api<MyRefund[]>("/me/refunds")]);
  if (!payments.ok || !refunds.ok) return <ErrorState title={t("loadError")} />;
  return (
    <div className="grid gap-6">
      <PageHeader title={t("paymentsTitle")} description={t("paymentsDescription")} />
      {payments.data.length === 0 ? (
        <EmptyState icon={Wallet} title={t("paymentsEmpty")} description={t("paymentsEmptyHint")} />
      ) : (
        <ul className="grid gap-2">
          {payments.data.map((p) => (
            <li key={p.id} className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border bg-card p-4">
              <div className="grid gap-0.5 text-sm">
                <span className="font-medium">
                  {paymentTypeLabel[p.type]} · {paymentMethodLabel[p.method]}
                </span>
                <span className="tabular text-muted-foreground">
                  {formatHotelLocal(p.paidAt)}
                  {p.bookingId && (
                    <>
                      {" · "}
                      <Link href={`/account/bookings/${p.bookingId}`} className="underline-offset-4 hover:underline">
                        {t("numberN", { number: p.bookingNumber ?? "" })}
                      </Link>
                    </>
                  )}
                </span>
              </div>
              <div className="flex items-center gap-2">
                <PaymentStatusBadge status={p.status} />
                <span className="tabular font-semibold">{formatMoney(p.amount)}</span>
              </div>
            </li>
          ))}
        </ul>
      )}
      {refunds.data.length > 0 && (
        <section aria-labelledby="refunds-title" className="grid gap-3">
          <h2 id="refunds-title" className="font-heading text-xl">
            {t("refunds")}
          </h2>
          <ul className="grid gap-2">
            {refunds.data.map((r) => (
              <li key={r.id} className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border bg-card p-4">
                <div className="grid gap-0.5 text-sm">
                  <span className="font-medium">{r.reason}</span>
                  <span className="tabular text-muted-foreground">
                    {formatHotelLocal(r.requestedAt)} · {t("numberN", { number: r.bookingNumber ?? "" })}
                  </span>
                  {r.decisionComment && <span className="text-muted-foreground">{r.decisionComment}</span>}
                </div>
                <div className="flex items-center gap-2">
                  <RefundStatusBadge status={r.status} />
                  <span className="tabular font-semibold">{formatMoney(r.amount)}</span>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
