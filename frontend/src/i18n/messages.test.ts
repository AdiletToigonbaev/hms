import { describe, expect, it } from "vitest";
import ru from "../../messages/ru.json";
import { defaultLocale, locales } from "./config";

function leaves(obj: Record<string, unknown>, prefix = ""): [string, unknown][] {
  return Object.entries(obj).flatMap(([key, value]) =>
    value !== null && typeof value === "object"
      ? leaves(value as Record<string, unknown>, `${prefix}${key}.`)
      : [[`${prefix}${key}`, value] as [string, unknown]],
  );
}

describe("i18n", () => {
  it("русский — язык по умолчанию", () => {
    expect(defaultLocale).toBe("ru");
    expect(locales).toContain("ru");
  });

  it("все сообщения — непустые строки", () => {
    for (const [key, value] of leaves(ru)) {
      expect(typeof value, key).toBe("string");
      expect((value as string).trim(), key).not.toBe("");
    }
  });
});

describe("подсказки настроек", () => {
  it("у каждой настройки есть пояснение и пример", () => {
    for (const [key, entry] of Object.entries(ru.SettingsHelp)) {
      expect(entry.text.length, key).toBeGreaterThan(20);
      expect(entry.example.length, key).toBeGreaterThan(10);
    }
  });
});
