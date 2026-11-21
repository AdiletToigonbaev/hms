import { describe, expect, it } from "vitest";
import { consentSchema, guestSchema } from "./guests";

const base = { fullName: "Айгуль", phone: "+996 700 000 001", whatsAppPhone: "", email: "", birthDate: "", notes: "" };

describe("guest validation", () => {
  it("accepts a guest with phone only and empties optional fields", () => {
    expect(guestSchema.parse(base)).toMatchObject({ fullName: "Айгуль", whatsAppPhone: null, email: null, birthDate: null, notes: null });
  });

  it("requires phone or email", () => {
    const result = guestSchema.safeParse({ ...base, phone: "" });
    expect(result.success).toBe(false);
    expect(result.error?.issues[0].path).toEqual(["phone"]);
    expect(guestSchema.safeParse({ ...base, phone: "", email: "a@example.com" }).success).toBe(true);
  });

  it("validates consent source", () => {
    expect(consentSchema.parse({ type: "WhatsApp", granted: "true", source: "FrontDesk" })).toEqual({ type: "WhatsApp", granted: true, source: "FrontDesk" });
    expect(consentSchema.safeParse({ type: "WhatsApp", granted: "true", source: "" }).success).toBe(false);
  });
});
