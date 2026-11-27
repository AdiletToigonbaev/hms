import { describe, expect, it } from "vitest";
import { formatPhone, messageStatusLabel, windowLeft } from "./labels";

describe("сообщения", () => {
  it("статусы доставки подписаны по-русски", () => {
    expect(messageStatusLabel.Read).toBe("Прочитано");
    expect(messageStatusLabel.Failed).toBe("Не отправлено");
  });

  it("остаток окна обслуживания WhatsApp", () => {
    const now = new Date("2026-11-27T10:00:00Z");
    expect(windowLeft("2026-11-27T15:20:00Z", now)).toBe("ещё 5 ч 20 мин");
    expect(windowLeft("2026-11-27T10:30:00Z", now)).toBe("ещё 30 мин");
    expect(windowLeft("2026-11-27T09:00:00Z", now)).toBeNull();
    expect(windowLeft(null, now)).toBeNull();
  });

  it("телефон Кыргызстана — группами цифр, остальные как есть", () => {
    expect(formatPhone("+996555123456")).toBe("+996 555 123 456");
    expect(formatPhone("+16505551234")).toBe("+16505551234");
  });
});
