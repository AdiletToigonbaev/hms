import { describe, expect, it } from "vitest";
import { formatHotelDateTime, formatHotelLocal, formatHotelRelative, formatMoney, formatStayDate } from "./format";

// Intl использует неразрывные пробелы — нормализуем для сравнения
const plain = (s: string) => s.replace(/[  ]/g, " ");

describe("formatMoney", () => {
  it("форматирует сумму в сомах с двумя знаками", () => {
    expect(plain(formatMoney(1234.5))).toBe("1 234,50 сом");
  });

  it("принимает строку numeric(14,2) с backend", () => {
    expect(plain(formatMoney("10500.00"))).toBe("10 500,00 сом");
  });

  it("отклоняет некорректную сумму", () => {
    expect(() => formatMoney("abc")).toThrow(RangeError);
  });
});

describe("formatStayDate", () => {
  it("не сдвигает дату проживания часовым поясом", () => {
    expect(formatStayDate("2026-12-31")).toBe("31.12.2026");
  });

  it("отклоняет не-ISO дату", () => {
    expect(() => formatStayDate("31.12.2026")).toThrow(RangeError);
  });
});

describe("formatHotelDateTime", () => {
  it("показывает время в часовом поясе отеля (UTC+6), а не устройства", () => {
    expect(plain(formatHotelDateTime("2026-11-30T19:30:00Z"))).toBe("01.12.2026, 01:30");
  });
});

describe("hotel local time", () => {
  it("formats API local time without time zone shifts", async () => {
    const { formatHotelLocal, formatHotelTime } = await import("./format");
    expect(formatHotelLocal("2026-12-04T22:00")).toBe("04.12.2026 22:00");
    expect(formatHotelTime("2026-12-04T09:00")).toBe("09:00");
    expect(() => formatHotelLocal("04.12.2026")).toThrow(RangeError);
  });
});

describe("formatHotelRelative", () => {
  it("сегодня, завтра, вчера и другие дни (в том числе через границу месяца)", () => {
    expect(formatHotelRelative("2026-11-22T08:00", "2026-11-22")).toBe("сегодня 08:00");
    expect(formatHotelRelative("2026-12-01T10:00", "2026-11-30")).toBe("завтра 10:00");
    expect(formatHotelRelative("2026-11-21T23:00", "2026-11-22")).toBe("вчера 23:00");
    expect(formatHotelRelative("2026-11-25T12:00", "2026-11-22")).toBe(formatHotelLocal("2026-11-25T12:00"));
  });
});
