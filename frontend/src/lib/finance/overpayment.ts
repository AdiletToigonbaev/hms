import type { BookingBalance } from "@/lib/api/types";

const refundable = new Set(["Paid", "PartiallyRefunded", "RefundPending"]);

export interface OverpaymentRefund {
  /** Переплата, на которую ещё нет запроса на возврат. */
  amount: number;
  /** Сумма в уже запрошенных, но не выплаченных возвратах. */
  pending: number;
  /** Платёж, с которого вернуть (последний, где хватает суммы; иначе — где можно вернуть больше всего). */
  paymentId: string | null;
  /** Сколько предложить вернуть с этого платежа. */
  suggested: number;
}

/** Переплата по брони и платёж, с которого её вернуть. Возврат всегда идёт с конкретного платежа и не больше его остатка. */
export function overpaymentRefund(b: BookingBalance): OverpaymentRefund | null {
  if (b.balance >= 0) return null;
  const pending = round(b.refunds.filter((r) => r.status === "Requested" || r.status === "Approved").reduce((s, r) => s + r.amount, 0));
  const amount = round(Math.max(0, -b.balance - pending));
  const candidates = b.payments
    .filter((p) => p.amount > 0 && refundable.has(p.status))
    .map((p) => ({
      id: p.id,
      paidAt: p.paidAt,
      available: round(p.amount - b.refunds.filter((r) => r.paymentId === p.id && r.status !== "Rejected").reduce((s, r) => s + r.amount, 0)),
    }))
    .filter((p) => p.available > 0)
    .sort((x, y) => y.paidAt.localeCompare(x.paidAt));
  const best = candidates.find((p) => p.available >= amount) ?? [...candidates].sort((x, y) => y.available - x.available)[0];
  return { amount, pending, paymentId: amount > 0 ? (best?.id ?? null) : null, suggested: best ? Math.min(amount, best.available) : 0 };
}

const round = (v: number) => Math.round(v * 100) / 100;
