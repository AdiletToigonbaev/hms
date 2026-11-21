import { describe, expect, it } from "vitest";
import { defaultPaymentType, paymentMethods, registrableTypes } from "./labels";

describe("finance labels", () => {
  it("offers all manual methods and never offers reversal for registration", () => {
    expect(paymentMethods).toEqual(["Cash", "BankTransfer", "CardPos", "MBank", "Optima"]);
    expect(registrableTypes).not.toContain("Reversal");
  });

  it("suggests the payment type from the booking", () => {
    expect(defaultPaymentType("Confirmed", "2026-11-22T10:00", "2026-11-21T14:00")).toBe("Prepayment");
    expect(defaultPaymentType("CheckedIn", "2026-11-21T10:00", "2026-11-21T14:00")).toBe("Stay");
    expect(defaultPaymentType("Cancelled", "2026-11-22T10:00", "2026-11-21T14:00")).toBe("Penalty");
    expect(defaultPaymentType("NoShow", "2026-11-20T10:00", "2026-11-21T14:00")).toBe("Penalty");
  });
});
