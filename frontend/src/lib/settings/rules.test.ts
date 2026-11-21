import { createTranslator } from "next-intl";
import { describe, expect, it } from "vitest";
import messages from "../../../messages/ru.json";
import { describeRules, type BookingRulesValues } from "./rules";

const t = createTranslator({ locale: "ru", messages, namespace: "SettingsSummary" }) as unknown as (
  key: string,
  values?: Record<string, string | number>,
) => string;

/** Значения по умолчанию: штрафов нет (решение заказчика 2026-11-21). */
const defaults: BookingRulesValues = {
  hourlyMinHours: 2,
  hourlyMaxHours: 12,
  hourlyFreeCancelMinutes: 120,
  hourlyLateCancelPenaltyHours: 0,
  hourlyNoShowAfterMinutes: 30,
  hourlyNoShowPenaltyHours: 0,
  hourlyPendingHoldMinutes: 30,
  hourlyRefundUnused: false,
  checkInTime: "14:00",
  checkOutTime: "12:00",
  noShowCutoffTime: "12:00",
  freeCancelHoursBeforeCheckIn: 24,
  lateCancelPenaltyNights: 0,
  noShowPenaltyNights: 0,
  pendingHoldHours: 24,
  earlyDeparturePenaltyNights: 0,
};

describe("describeRules", () => {
  it("описывает правила по умолчанию (без штрафов) обычными словами с правильными окончаниями", () => {
    const { hourly, nightly } = describeRules(defaults, t);
    expect(hourly).toEqual([
      "По часам: бронь от 2 часов до 12 часов, начало — в любой целый час.",
      "Отмена всегда бесплатна — штраф за позднюю отмену не задан.",
      "Если гость не пришёл через 30 минут после начала — бронь «Неявка», номер освобождается, без штрафа.",
      "Неподтверждённая бронь держит номер 30 минут, но не дольше начала брони.",
      "При досрочном выезде неиспользованные часы не возвращаются.",
    ]);
    expect(nightly[0]).toBe("По суткам: заезд с 14:00, выезд до 12:00.");
    expect(nightly[1]).toBe("Отмена всегда бесплатна — штраф за позднюю отмену не задан.");
    expect(nightly[4]).toBe("При досрочном выезде гость платит только за прожитые ночи.");
  });

  it("меняется вместе с цифрами, когда владелец включает штрафы", () => {
    const { hourly, nightly } = describeRules(
      { ...defaults, hourlyFreeCancelMinutes: 90, hourlyLateCancelPenaltyHours: 1, hourlyNoShowPenaltyHours: 1, lateCancelPenaltyNights: 1, hourlyRefundUnused: true, earlyDeparturePenaltyNights: 2 },
      t,
    );
    expect(hourly[1]).toBe("Бесплатная отмена — не позже чем за 90 минут до начала; позже — штраф 1 час.");
    expect(hourly[2]).toBe("Если гость не пришёл через 30 минут после начала — бронь «Неявка», номер освобождается, штраф 1 час.");
    expect(nightly[1]).toBe("Бесплатная отмена — не позже чем за 24 часа до заезда; позже — штраф 1 ночь.");
    expect(describeRules({ ...defaults, hourlyMinHours: 1 }, t).hourly[0]).toContain("от 1 часа");
    expect(hourly[4]).toBe("При досрочном выезде неиспользованные часы возвращаются.");
    expect(nightly[4]).toBe("При досрочном выезде — штраф 2 ночи.");
  });
});
