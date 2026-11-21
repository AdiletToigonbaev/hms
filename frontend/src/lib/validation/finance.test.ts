import { describe, expect, it } from "vitest";
import { expenseSchema, paymentSchema, refundRequestSchema } from "./finance";

describe("finance validation", () => {
  it("accepts comma decimals and spaces in amounts", () => {
    const r = paymentSchema.safeParse({ type: "Stay", method: "Cash", amount: "1 500,50" });
    expect(r.success && r.data.amount).toBe(1500.5);
  });

  it("rejects zero, negative and three-decimal amounts", () => {
    for (const amount of ["0", "-5", "10,555", "abc"]) {
      expect(paymentSchema.safeParse({ type: "Stay", method: "Cash", amount }).success).toBe(false);
    }
  });

  it("never lets the form register a reversal", () => {
    expect(paymentSchema.safeParse({ type: "Reversal", method: "Cash", amount: "100" }).success).toBe(false);
  });

  it("requires a reason for refunds and a description for expenses", () => {
    expect(refundRequestSchema.safeParse({ paymentId: crypto.randomUUID(), amount: "100", reason: " ", method: "Cash" }).success).toBe(false);
    expect(
      expenseSchema.safeParse({ categoryId: crypto.randomUUID(), amount: "100", date: "2026-11-21", method: "Cash", description: "" }).success,
    ).toBe(false);
  });
});
