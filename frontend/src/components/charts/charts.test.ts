import { describe, expect, it } from "vitest";
import { axisLabel, niceTicks } from "./charts";

describe("шкала графиков", () => {
  it("круглые шаги 1–2–5 и верх не ниже максимума", () => {
    expect(niceTicks(9500)).toEqual([0, 5000, 10000]);
    expect(niceTicks(130)).toEqual([0, 50, 100, 150]);
    expect(niceTicks(0)).toEqual([0]);
  });

  it("подписи: целиком до 10 000, дальше компактно; проценты", () => {
    expect(axisLabel(5000)).toBe(new Intl.NumberFormat("ru-RU").format(5000));
    expect(axisLabel(25000)).toBe("25 тыс.");
    expect(axisLabel(1_500_000)).toBe("1,5 млн");
    expect(axisLabel(0.5, true)).toBe("50%");
  });
});
