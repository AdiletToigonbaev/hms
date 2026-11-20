import { describe, expect, it } from "vitest";
import { bedTypeLabel, bedTypes, groupByFloor, plural, roomStatusLabel, roomStatusTone } from "./status";

describe("статусы номеров", () => {
  it("у каждого статуса есть подпись и цвет (цвет всегда дублируется текстом)", () => {
    for (const status of Object.keys(roomStatusLabel)) {
      expect(roomStatusLabel[status as keyof typeof roomStatusLabel]).toBeTruthy();
      expect(roomStatusTone[status as keyof typeof roomStatusTone]).toMatch(/status-/);
    }
  });

  it("типы кроватей совпадают с backend", () => {
    expect(bedTypes).toEqual(["Single", "Double", "Twin", "Queen", "King", "Bunk"]);
    expect(bedTypeLabel.Double).toBe("Двуспальная");
  });
});

describe("plural", () => {
  it.each([
    [1, "гость"],
    [2, "гостя"],
    [5, "гостей"],
    [11, "гостей"],
    [21, "гость"],
    [22, "гостя"],
    [112, "гостей"],
  ])("%i → %s", (n, expected) => {
    expect(plural(n, "гость", "гостя", "гостей")).toBe(expected);
  });
});

describe("groupByFloor", () => {
  it("сортирует этажи по возрастанию, номера без этажа — в конце", () => {
    const rooms = [{ floor: 2, n: "201" }, { floor: null, n: "A" }, { floor: 1, n: "101" }, { floor: 2, n: "202" }, { floor: -1, n: "B1" }];
    expect(groupByFloor(rooms).map(([floor, list]) => [floor, list.map((r) => r.n)])).toEqual([
      [-1, ["B1"]],
      [1, ["101"]],
      [2, ["201", "202"]],
      [null, ["A"]],
    ]);
  });
});

describe("roomBookingHint", () => {
  it("shows current booking end or next booking time", async () => {
    const { roomBookingHint } = await import("./status");
    expect(roomBookingHint({ occupiedUntil: "2026-11-21T14:30", nextBookingAt: "2026-11-21T18:00" }, "2026-11-21")).toBe("по брони до 14:30");
    expect(roomBookingHint({ occupiedUntil: null, nextBookingAt: "2026-11-21T15:00" }, "2026-11-21")).toBe("бронь в 15:00");
    expect(roomBookingHint({ occupiedUntil: "2026-11-22T12:00", nextBookingAt: null }, "2026-11-21")).toBe("по брони до 22.11 12:00");
    expect(roomBookingHint({ occupiedUntil: null, nextBookingAt: null }, "2026-11-21")).toBeNull();
  });
});
