import { describe, expect, it } from "vitest";
import { config } from "./proxy";

// Регрессия: с "\\." в matcher экранирование терялось при сборке, и proxy не запускался ни для одной страницы.
describe("proxy matcher", () => {
  const pattern = config.matcher[0];
  const matches = (path: string) => new RegExp(`^${pattern}$`).test(path);

  it("не содержит обратных слэшей (они теряются при сборке)", () => {
    expect(pattern).not.toContain("\\");
  });

  it("применяется к страницам приложения", () => {
    for (const path of ["/", "/owner", "/owner/staff/123", "/login", "/account", "/change-password"]) {
      expect(matches(path), path).toBe(true);
    }
  });

  it("не применяется к статике и собственным API-маршрутам", () => {
    for (const path of ["/api/health", "/_next/static/chunk.js", "/_next/image", "/favicon.ico", "/logo.png", "/robots.txt"]) {
      expect(matches(path), path).toBe(false);
    }
  });
});
