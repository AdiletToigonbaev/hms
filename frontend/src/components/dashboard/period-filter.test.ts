import { describe, expect, it } from "vitest";
import { readHour } from "./period-filter";

describe("часы в фильтре периода", () => {
  it("1–23 — час; 00:00, 24:00, мусор — сутки целиком (null)", () => {
    expect(readHour("6")).toBe(6);
    expect(readHour("22")).toBe(22);
    expect(readHour("0")).toBeNull();
    expect(readHour("24")).toBeNull();
    expect(readHour("6.5")).toBeNull();
    expect(readHour(undefined)).toBeNull();
    expect(readHour(["6"])).toBeNull();
  });
});
