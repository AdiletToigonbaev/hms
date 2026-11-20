import { describe, expect, it } from "vitest";
import { createBookingSchema, discountSchema, rateSchema, staySchema } from "./bookings";

const booking = {
  roomId: "0196d3b0-0000-7000-8000-000000000001",
  checkIn: "2026-12-01",
  checkOut: "2026-12-03",
  adults: "2",
  children: "0",
  guestName: "Асель",
  guestPhone: "+996 555 123 456",
  guestEmail: "",
  comment: "",
  promoCode: "vip",
};

describe("booking validation", () => {
  it("accepts a valid booking and normalises promo code", () => {
    const parsed = createBookingSchema.parse(booking);
    expect(parsed.promoCode).toBe("VIP");
    expect(parsed.guestEmail).toBeNull();
    expect(parsed.adults).toBe(2);
  });

  it("rejects checkout before checkin and too long stays", () => {
    expect(staySchema.safeParse({ ...booking, checkOut: "2026-12-01" }).success).toBe(false);
    expect(staySchema.safeParse({ ...booking, checkOut: "2027-02-15" }).success).toBe(false);
  });

  it("requires phone or email", () => {
    const result = createBookingSchema.safeParse({ ...booking, guestPhone: "", guestEmail: "" });
    expect(result.success).toBe(false);
    expect(result.error?.issues[0].path).toEqual(["guestPhone"]);
  });
});

describe("pricing validation", () => {
  it("limits percent discounts and validates promo codes", () => {
    const base = { name: "Скидка", code: "", type: "Percent", value: "10", validFrom: "", validTo: "", minNights: "" };
    expect(discountSchema.parse(base)).toMatchObject({ code: null, value: 10, minNights: 0, validFrom: null });
    expect(discountSchema.safeParse({ ...base, value: "150" }).success).toBe(false);
    expect(discountSchema.safeParse({ ...base, code: "a b" }).success).toBe(false);
  });

  it("requires rate period order", () => {
    const rate = { roomTypeId: booking.roomId, name: "Сезон", dateFrom: "2026-12-10", dateTo: "2026-12-01", price: "4000", priority: "1" };
    expect(rateSchema.safeParse(rate).success).toBe(false);
    expect(rateSchema.safeParse({ ...rate, dateTo: "2026-12-31" }).success).toBe(true);
  });
});
