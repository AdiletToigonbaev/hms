import { describe, expect, it } from "vitest";
import type { CalendarBooking } from "@/lib/api/types";
import { conflictsWith, dayMovements, fromMinutes, moveStep, rescheduleTarget, resizeStep, snap, span, targetPeriod, toMinutes } from "./timeline";

const limits = { minHours: 2, maxHours: 12 };

function booking(over: Partial<CalendarBooking>): CalendarBooking {
  return {
    id: "b1",
    number: "26-000001",
    roomId: "r1",
    status: "Confirmed",
    kind: "Hourly",
    startsAt: "2026-11-21T10:00",
    endsAt: "2026-11-21T12:00",
    blockedUntil: "2026-11-21T12:30",
    guestName: "Гость",
    guestPhone: null,
    adults: 2,
    children: 0,
    hours: 2,
    nights: 0,
    totalAmount: 1000,
    holdUntil: null,
    noShowDue: false,
    canReschedule: true,
    version: 1,
    ...over,
  };
}

describe("timeline", () => {
  it("converts local time to minutes and back", () => {
    expect(fromMinutes(toMinutes("2026-11-21T23:30") + 60)).toBe("2026-11-22T00:30");
  });

  it("places a block on the day scale", () => {
    // 1 px в минуту: 10:00 → 600 px, 2 часа → 120 px.
    expect(span("2026-11-21T10:00", "2026-11-21T12:00", "2026-11-21", 1, 1)).toEqual({ left: 600, width: 120, clippedStart: false, clippedEnd: false });
  });

  it("clips blocks crossing the scale edges and hides blocks outside", () => {
    const s = span("2026-11-20T14:00", "2026-11-22T12:00", "2026-11-21", 1, 1)!;
    expect(s).toMatchObject({ left: 0, width: 1440, clippedStart: true, clippedEnd: true });
    expect(span("2026-11-19T14:00", "2026-11-20T12:00", "2026-11-21", 1, 1)).toBeNull();
  });

  it("snaps to the nearest step", () => {
    expect(snap(89, 60)).toBe(60);
    expect(snap(91, 60)).toBe(120);
    expect(snap(-29, 60)).toBe(0);
  });

  it("chooses drag steps by kind and view", () => {
    expect(moveStep("Hourly", "day")).toBe(60);
    expect(moveStep("Hourly", "week")).toBe(1440);
    expect(moveStep("Nightly", "day")).toBeNull();
    expect(resizeStep("Hourly", "week")).toBeNull();
    expect(resizeStep("Nightly", "week")).toBe(1440);
  });

  it("builds an hourly reschedule request, crossing midnight", () => {
    const b = booking({ startsAt: "2026-11-21T22:00", endsAt: "2026-11-22T00:00" });
    expect(rescheduleTarget(b, { roomId: "r2", shift: 120, stretch: 60 }, limits)).toEqual({
      kind: "Hourly",
      roomId: "r2",
      date: "2026-11-22",
      startHour: 0,
      hours: 3,
    });
  });

  it("returns null when nothing changed or hours break the rules", () => {
    const b = booking({});
    expect(rescheduleTarget(b, { roomId: "r1", shift: 0, stretch: 0 }, limits)).toBeNull();
    expect(rescheduleTarget(b, { roomId: "r1", shift: 0, stretch: -60 }, limits)).toBeNull();
  });

  it("builds a nightly reschedule request by days", () => {
    const b = booking({ kind: "Nightly", startsAt: "2026-11-21T14:00", endsAt: "2026-11-23T12:00", hours: null, nights: 2 });
    expect(rescheduleTarget(b, { roomId: "r1", shift: 1440, stretch: 1440 }, limits)).toEqual({
      kind: "Nightly",
      roomId: "r1",
      checkIn: "2026-11-22",
      checkOut: "2026-11-25",
    });
    expect(rescheduleTarget(b, { roomId: "r1", shift: 0, stretch: -2880 }, limits)).toBeNull();
  });

  it("lists arrivals, departures, late guests and awaiting confirmation for a day", () => {
    const list = [
      booking({ id: "a", startsAt: "2026-11-21T15:00", endsAt: "2026-11-21T17:00" }),
      booking({ id: "d", status: "CheckedIn", startsAt: "2026-11-20T14:00", endsAt: "2026-11-21T12:00" }),
      booking({ id: "l", noShowDue: true }),
      booking({ id: "p", status: "Pending", holdUntil: "2026-11-21T10:30" }),
      booking({ id: "x", status: "CheckedOut", startsAt: "2026-11-21T08:00", endsAt: "2026-11-21T09:00" }),
    ];
    const m = dayMovements(list, "2026-11-21");
    expect(m.arrivals.map((b) => b.id)).toEqual(["l", "p", "a"]);
    expect(m.departures.map((b) => b.id)).toEqual(["d", "l", "a"]);
    expect(m.late.map((b) => b.id)).toEqual(["l"]);
    expect(m.awaiting.map((b) => b.id)).toEqual(["p"]);
  });
});

describe("conflicts", () => {
  const times = { checkInTime: "14:00", checkOutTime: "12:00" };
  const other = booking({ id: "o", roomId: "r2", startsAt: "2026-11-21T14:00", endsAt: "2026-11-21T16:00", blockedUntil: "2026-11-21T16:30" });

  it("counts cleaning after the moved booking", () => {
    const moving = booking({});
    // 12:00–14:00 + 30 мин уборки задевает бронь с 14:00.
    const period = targetPeriod({ kind: "Hourly", roomId: "r2", date: "2026-11-21", startHour: 12, hours: 2 }, moving, times);
    expect(conflictsWith([moving, other], moving.id, "r2", period)).toBe(true);
    const later = targetPeriod({ kind: "Hourly", roomId: "r2", date: "2026-11-21", startHour: 17, hours: 2 }, moving, times);
    expect(conflictsWith([moving, other], moving.id, "r2", later)).toBe(false);
  });

  it("ignores the booking itself and checked-out guests", () => {
    const moving = booking({ roomId: "r2" });
    const done = booking({ id: "d", roomId: "r2", status: "CheckedOut" });
    const period = targetPeriod({ kind: "Hourly", roomId: "r2", date: "2026-11-21", startHour: 10, hours: 3 }, moving, times);
    expect(conflictsWith([moving, done], moving.id, "r2", period)).toBe(false);
  });
});
