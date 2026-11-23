import { describe, expect, it } from "vitest";
import { readStay, stayApiBody, stayQuery, staySummary, validateStay } from "./stay";

const today = "2026-11-22";

describe("readStay — параметры поиска сайта", () => {
  it("по умолчанию: почасовая бронь сегодня с ближайшего часа, минимум часов, двое взрослых", () => {
    const s = readStay({}, today, 14, 2, 12);
    expect(s).toMatchObject({ hourly: true, date: today, startHour: 15, hours: 2, adults: 2, children: 0, roomTypeId: null, roomId: null });
  });

  it("после 23:00 — завтра с 10:00", () => {
    expect(readStay({}, today, 23, 2, 12)).toMatchObject({ date: "2026-11-23", startHour: 10 });
  });

  it("прошедшие и неверные значения заменяются, а не ломают страницу", () => {
    const s = readStay({ date: "2020-01-01", startHour: "3", hours: "99", adults: "-1", roomTypeId: "<script>", roomId: "1 OR 1=1" }, today, 14, 2, 12);
    expect(s).toMatchObject({ date: today, startHour: 15, hours: 2, adults: 2, roomTypeId: null, roomId: null });
  });

  it("посуточно: выезд не раньше следующего дня", () => {
    const s = readStay({ kind: "nightly", checkIn: "2026-11-25", checkOut: "2026-11-24" }, today, 10, 2, 12);
    expect(s).toMatchObject({ hourly: false, checkIn: "2026-11-25", checkOut: "2026-11-26" });
    expect(validateStay(s)).toBeNull();
  });

  it("ссылки и запросы к API сохраняют выбор", () => {
    const s = readStay({ kind: "hourly", date: "2026-11-23", startHour: "18", hours: "3", adults: "1" }, today, 14, 2, 12);
    expect(stayQuery(s, { step: "confirm" }).toString()).toBe("kind=hourly&date=2026-11-23&startHour=18&hours=3&adults=1&children=0&step=confirm");
    expect(stayApiBody(s)).toEqual({ kind: "Hourly", date: "2026-11-23", startHour: 18, hours: 3, adults: 1, children: 0 });
    expect(staySummary(s, (d) => d, (n) => `${n} н`)).toBe("2026-11-23, 18:00–21:00 · 3 ч");
  });
});
