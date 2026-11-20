import { describe, expect, it } from "vitest";
import {
  dayRole,
  daysBetween,
  formatDayShort,
  formatMonthTitle,
  isIsoDate,
  monthGrid,
  monthStart,
  nextWeekend,
  pickDay,
  shiftDays,
  weekdayIndex,
} from "./calendar";

describe("calendar dates", () => {
  it("validates ISO dates including impossible days", () => {
    expect(isIsoDate("2026-12-20")).toBe(true);
    expect(isIsoDate("2026-02-30")).toBe(false);
    expect(isIsoDate("20.12.2026")).toBe(false);
    expect(isIsoDate(undefined)).toBe(false);
  });

  it("shifts days and months across boundaries", () => {
    expect(shiftDays("2026-12-31", 1)).toBe("2027-01-01");
    expect(daysBetween("2026-12-20", "2026-12-23")).toBe(3);
    expect(monthStart("2026-12-20", 1)).toBe("2027-01-01");
    expect(monthStart("2026-01-31", -1)).toBe("2025-12-01");
  });

  it("builds a Monday-first 6-week grid", () => {
    // 1 декабря 2026 — вторник
    const grid = monthGrid("2026-12-01");
    expect(grid).toHaveLength(6);
    expect(grid[0][0]).toBeNull();
    expect(grid[0][1]).toBe("2026-12-01");
    expect(grid.flat().filter(Boolean)).toHaveLength(31);
    expect(weekdayIndex("2026-12-20")).toBe(6); // воскресенье
  });

  it("formats in Russian", () => {
    expect(formatMonthTitle("2026-12-01")).toBe("Декабрь 2026");
    expect(formatDayShort("2026-12-20")).toBe("20 дек");
  });
});

describe("range selection", () => {
  it("picks start, then end after start", () => {
    let r = pickDay({ start: null, end: null }, "2026-12-20", false);
    expect(r).toEqual({ start: "2026-12-20", end: null });
    r = pickDay(r, "2026-12-23", false);
    expect(r).toEqual({ start: "2026-12-20", end: "2026-12-23" });
    // новый клик после готового периода начинает заново
    expect(pickDay(r, "2026-12-25", false)).toEqual({ start: "2026-12-25", end: null });
  });

  it("moves start when clicking earlier; same day only for filters", () => {
    expect(pickDay({ start: "2026-12-20", end: null }, "2026-12-18", false)).toEqual({ start: "2026-12-18", end: null });
    expect(pickDay({ start: "2026-12-20", end: null }, "2026-12-20", false)).toEqual({ start: "2026-12-20", end: null });
    expect(pickDay({ start: "2026-12-20", end: null }, "2026-12-20", true)).toEqual({ start: "2026-12-20", end: "2026-12-20" });
  });

  it("marks roles including hover preview", () => {
    const r = { start: "2026-12-20", end: null };
    expect(dayRole("2026-12-20", r, null)).toBe("single");
    expect(dayRole("2026-12-21", r, "2026-12-22")).toBe("inside");
    expect(dayRole("2026-12-22", r, "2026-12-22")).toBe("end");
    expect(dayRole("2026-12-20", r, "2026-12-22")).toBe("start");
  });

  it("finds the next weekend (Fri → Sun nights)", () => {
    expect(nextWeekend("2026-12-20")).toEqual({ start: "2026-12-25", end: "2026-12-27" }); // вс → пт 25
    expect(nextWeekend("2026-12-25")).toEqual({ start: "2026-12-25", end: "2026-12-27" });
  });
});
