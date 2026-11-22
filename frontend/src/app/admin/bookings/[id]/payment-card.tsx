import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { PaymentStatusBadge, RefundStatusBadge } from "@/components/finance-badges";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { BookingBalance } from "@/lib/api/types";
import { defaultPaymentType, paymentMethodLabel, paymentTypeLabel } from "@/lib/finance/labels";
import { formatHotelLocal, formatMoney } from "@/lib/format";
import { overpaymentRefund } from "@/lib/finance/overpayment";
import { cn } from "@/lib/utils";
import { buttonVariants } from "@/components/ui/button";
import { RegisterPaymentForm } from "../../payments/finance-forms";

/** Оплата брони: к оплате, получено, остаток; платежи и возвраты; приём оплаты (если есть остаток и право). */
export async function PaymentCard({
  balance: b,
  startsAt,
  now,
  canRegister,
  canRefund = false,
}: {
  balance: BookingBalance;
  startsAt: string;
  now: string;
  canRegister: boolean;
  canRefund?: boolean;
}) {
  const t = await getTranslations("Finance");
  const over = overpaymentRefund(b);
  const rows = [
    { label: t("due"), value: b.due },
    { label: t("received"), value: b.received },
    ...(b.refunded > 0 ? [{ label: t("refunded"), value: -b.refunded }] : []),
  ];
  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-xl">{t("bookingPayment")}</h2>
        </CardTitle>
        <CardDescription>{t("bookingPaymentHint")}</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-5">
        <dl className="grid gap-1.5 text-sm">
          {rows.map((r) => (
            <div key={r.label} className="flex justify-between gap-3">
              <dt className="text-muted-foreground">{r.label}</dt>
              <dd className="tabular">{formatMoney(r.value)}</dd>
            </div>
          ))}
          <div className="mt-1 flex justify-between gap-3 border-t pt-2">
            <dt className="font-medium">{b.balance > 0 ? t("toPay") : b.balance < 0 ? t("overpaid") : t("settled")}</dt>
            <dd className={cn("tabular font-heading text-xl", b.balance > 0 && "text-destructive", b.balance < 0 && "text-status-reserved")}>
              {formatMoney(Math.abs(b.balance))}
            </dd>
          </div>
          {b.prepaymentRequired > 0 && b.received < b.prepaymentRequired && (
            <p className="text-xs text-muted-foreground">{t("prepaymentHint", { amount: formatMoney(b.prepaymentRequired) })}</p>
          )}
        </dl>
        {over && (
          <div className="grid gap-2 rounded-xl border border-status-reserved/30 bg-status-reserved/5 p-3 text-sm">
            {over.amount > 0 ? (
              <>
                <p>
                  {over.pending > 0
                    ? t("overpaidPartlyRequested", { pending: formatMoney(over.pending), amount: formatMoney(over.amount) })
                    : t("overpaidHint")}
                </p>
                {canRefund && over.paymentId ? (
                  <div>
                    <Link href={`/admin/payments/${over.paymentId}?refund=${over.suggested}#refund`} className={buttonVariants({ size: "sm" })}>
                      {t("refundOverpaid", { amount: formatMoney(over.suggested) })}
                    </Link>
                    {over.suggested < over.amount && <p className="mt-1 text-xs text-muted-foreground">{t("refundOverpaidSplit")}</p>}
                  </div>
                ) : (
                  !canRefund && <p className="text-xs text-muted-foreground">{t("refundNoRight")}</p>
                )}
              </>
            ) : (
              <p>{t("overpaidRequested", { amount: formatMoney(over.pending) })}</p>
            )}
          </div>
        )}

        {b.payments.length > 0 && (
          <section className="grid gap-2" aria-labelledby="payments-title">
            <h3 id="payments-title" className="font-heading text-base">
              {t("payments")}
            </h3>
            <ul className="grid gap-1.5">
              {b.payments.map((p) => (
                <li key={p.id}>
                  <Link
                    href={`/admin/payments/${p.id}`}
                    className="grid gap-0.5 rounded-xl border px-3 py-2 text-sm transition hover:border-primary/40 hover:bg-accent/40"
                  >
                    <span className="flex flex-wrap items-center justify-between gap-2">
                      <span className="font-medium">
                        {paymentTypeLabel[p.type]} · {paymentMethodLabel[p.method]}
                      </span>
                      <span className={cn("tabular font-medium", p.amount < 0 && "text-destructive")}>{formatMoney(p.amount)}</span>
                    </span>
                    <span className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
                      <span>
                        {formatHotelLocal(p.paidAt)}
                        {p.registeredByName ? ` · ${p.registeredByName}` : ""}
                      </span>
                      <PaymentStatusBadge status={p.status} />
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        )}

        {b.refunds.length > 0 && (
          <section className="grid gap-2" aria-labelledby="refunds-title">
            <h3 id="refunds-title" className="font-heading text-base">
              {t("refunds")}
            </h3>
            <ul className="grid gap-1.5">
              {b.refunds.map((r) => (
                <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 rounded-xl border px-3 py-2 text-sm">
                  <span>
                    <span className="tabular font-medium">{formatMoney(r.amount)}</span> · {r.reason}
                  </span>
                  <RefundStatusBadge status={r.status} />
                </li>
              ))}
            </ul>
          </section>
        )}

        {canRegister && b.balance > 0 && (
          <section className="grid gap-2" aria-labelledby="register-title">
            <h3 id="register-title" className="font-heading text-base">
              {t("registerTitle")}
            </h3>
            <RegisterPaymentForm bookingId={b.bookingId} balance={b.balance} defaultType={defaultPaymentType(b.bookingStatus, startsAt, now)} />
          </section>
        )}
      </CardContent>
    </Card>
  );
}
