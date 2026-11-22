import { describe, expect, it } from "vitest";
import type { BookingBalance } from "@/lib/api/types";
import { overpaymentRefund } from "./overpayment";

const payment = (id: string, amount: number, paidAt: string, status = "Paid") => ({ id, amount, paidAt, status }) as BookingBalance["payments"][number];
const refund = (paymentId: string, amount: number, status: string) =>
  ({ id: paymentId + amount, paymentId, amount, status }) as BookingBalance["refunds"][number];
const balance = (b: Partial<BookingBalance>) => ({ balance: 0, payments: [], refunds: [], ...b }) as BookingBalance;

describe("overpaymentRefund", () => {
  it("нет переплаты — нечего возвращать", () => {
    expect(overpaymentRefund(balance({ balance: 100 }))).toBeNull();
  });

  it("берёт последний платёж, где хватает суммы", () => {
    const r = overpaymentRefund(balance({ balance: -375, payments: [payment("a", 1000, "2026-11-21T10:00"), payment("b", 200, "2026-11-21T12:00")] }));
    expect(r).toEqual({ amount: 375, pending: 0, paymentId: "a", suggested: 375 });
  });

  it("учитывает уже запрошенные возвраты и отклонённые не считает", () => {
    const r = overpaymentRefund(
      balance({ balance: -375, payments: [payment("a", 1000, "2026-11-21T10:00")], refunds: [refund("a", 300, "Requested"), refund("a", 500, "Rejected")] }),
    );
    expect(r).toEqual({ amount: 75, pending: 300, paymentId: "a", suggested: 75 });
  });

  it("возврат уже запрошен на всю переплату — платёж не предлагается", () => {
    const r = overpaymentRefund(balance({ balance: -375, payments: [payment("a", 1000, "2026-11-21T10:00")], refunds: [refund("a", 375, "Approved")] }));
    expect(r?.amount).toBe(0);
    expect(r?.paymentId).toBeNull();
  });

  it("если ни на одном платеже не хватает — предлагает максимум с самого крупного", () => {
    const r = overpaymentRefund(
      balance({
        balance: -500,
        payments: [payment("a", 300, "2026-11-21T10:00"), payment("b", 200, "2026-11-21T12:00"), payment("c", 900, "2026-11-21T13:00", "Reversed")],
      }),
    );
    expect(r).toEqual({ amount: 500, pending: 0, paymentId: "a", suggested: 300 });
  });
});
