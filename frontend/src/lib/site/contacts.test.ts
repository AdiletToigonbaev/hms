import { describe, expect, it } from "vitest";
import { twoGisLink, whatsAppLink } from "./contacts";

describe("whatsAppLink", () => {
  it("wa.me с цифрами номера и приветствием", () => {
    expect(whatsAppLink("+996700123456", "Здравствуйте! Хочу узнать о бронировании в Great Hotel.")).toBe(
      "https://wa.me/996700123456?text=%D0%97%D0%B4%D1%80%D0%B0%D0%B2%D1%81%D1%82%D0%B2%D1%83%D0%B9%D1%82%D0%B5!%20%D0%A5%D0%BE%D1%87%D1%83%20%D1%83%D0%B7%D0%BD%D0%B0%D1%82%D1%8C%20%D0%BE%20%D0%B1%D1%80%D0%BE%D0%BD%D0%B8%D1%80%D0%BE%D0%B2%D0%B0%D0%BD%D0%B8%D0%B8%20%D0%B2%20Great%20Hotel.",
    );
  });

  it("без номера — ссылки нет", () => {
    expect(whatsAppLink(null, "x")).toBeNull();
    expect(whatsAppLink("", "x")).toBeNull();
    expect(whatsAppLink("12", "x")).toBeNull();
  });
});

describe("twoGisLink", () => {
  it("карточка и короткая ссылка 2ГИС", () => {
    expect(twoGisLink("https://2gis.kg/bishkek/firm/70000001234567")).toBe("https://2gis.kg/bishkek/firm/70000001234567");
    expect(twoGisLink("https://go.2gis.com/abcd1")).toBe("https://go.2gis.com/abcd1");
  });

  it("чужие и небезопасные адреса не показываются", () => {
    for (const bad of [
      "http://2gis.kg/firm/1",
      "https://2gis.kg.evil.example/firm/1",
      "https://evil.example/?u=https://2gis.kg",
      "javascript:alert(1)",
      "https://user@2gis.kg/firm/1",
      "не ссылка",
      null,
    ]) {
      expect(twoGisLink(bad), String(bad)).toBeNull();
    }
  });
});
