import { describe, expect, it } from "vitest";
import { ACCESS_COOKIE, MUST_CHANGE_COOKIE, REFRESH_COOKIE, sessionCookies } from "./cookies";

const tokens = {
  accessToken: "a",
  accessTokenExpiresAt: "2026-11-20T12:15:00Z",
  refreshToken: "r",
  refreshTokenExpiresAt: "2026-12-04T12:00:00Z",
};

describe("sessionCookies", () => {
  it("все cookie httpOnly, SameSite=Lax и истекают вместе с токенами", () => {
    const list = sessionCookies({ ...tokens, user: { mustChangePassword: false } });
    expect(list.every((c) => c.httpOnly && c.sameSite === "lax" && c.path === "/")).toBe(true);
    expect(list.find((c) => c.name === ACCESS_COOKIE)?.expires.toISOString()).toBe("2026-11-20T12:15:00.000Z");
    expect(list.find((c) => c.name === REFRESH_COOKIE)?.expires.toISOString()).toBe("2026-12-04T12:00:00.000Z");
  });

  it("признак временного пароля ставится и снимается", () => {
    const set = sessionCookies({ ...tokens, user: { mustChangePassword: true } }).find((c) => c.name === MUST_CHANGE_COOKIE)!;
    const cleared = sessionCookies({ ...tokens, user: { mustChangePassword: false } }).find(
      (c) => c.name === MUST_CHANGE_COOKIE,
    )!;
    expect(set.value).toBe("1");
    expect(cleared.expires.getTime()).toBe(0);
  });
});
