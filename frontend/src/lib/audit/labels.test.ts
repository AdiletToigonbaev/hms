import { describe, expect, it } from "vitest";
import { auditEntityHref, auditLabel } from "./labels";

describe("журнал действий", () => {
  it("подпись действия по-русски, неизвестный код — как есть", () => {
    expect(auditLabel("booking.checked_in")).toBe("Заселение");
    expect(auditLabel("something.new")).toBe("something.new");
  });

  it("ссылка на объект — только для известных типов и корректного id", () => {
    const id = "01a1f99b-5396-70da-a2cf-1d85306e5294";
    expect(auditEntityHref("Booking", id)).toBe(`/admin/bookings/${id}`);
    expect(auditEntityHref("Report", "financial")).toBeNull();
    expect(auditEntityHref("Booking", "../../etc")).toBeNull();
  });
});
