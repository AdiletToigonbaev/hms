import { describe, expect, it } from "vitest";
import { addDays, describeDays, hotelToday, maskFromDays, nightsLabel } from "./status";

describe("bookings helpers", () => {
  it("builds and describes weekday masks like the backend", () => {
    expect(maskFromDays(["16", "32"])).toBe(48);
    expect(maskFromDays(["1", "999", "x"])).toBe(1);
    expect(describeDays(127)).toBe("Все дни");
    expect(describeDays(48)).toBe("Пт, Сб");
    expect(describeDays(1 | 64)).toBe("Пн, Вс");
  });

  it("adds days across month and year boundaries", () => {
    expect(addDays("2026-12-31", 1)).toBe("2027-01-01");
    expect(addDays("2026-03-01", -1)).toBe("2026-02-28");
  });

  it("uses the hotel time zone for today", () => {
    // 20:00 UTC 20 ноября = 02:00 21 ноября в Бишкеке (UTC+6)
    expect(hotelToday("Asia/Bishkek", new Date("2026-11-20T20:00:00Z"))).toBe("2026-11-21");
  });

  it("declines nights", () => {
    expect([1, 2, 5, 11, 21, 22].map(nightsLabel)).toEqual(["1 ночь", "2 ночи", "5 ночей", "11 ночей", "21 ночь", "22 ночи"]);
  });
});
