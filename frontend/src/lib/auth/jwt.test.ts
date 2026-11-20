import { describe, expect, it } from "vitest";
import { isExpiring, readAccessToken } from "./jwt";

function token(payload: object): string {
  const b64 = (o: object) => Buffer.from(JSON.stringify(o)).toString("base64url");
  return `${b64({ alg: "HS256", typ: "JWT" })}.${b64(payload)}.signature`;
}

describe("readAccessToken", () => {
  it("читает sub, роли (одна или массив) и срок", () => {
    expect(readAccessToken(token({ sub: "u1", role: "Owner", exp: 100, name: "Асан" }))).toEqual({
      sub: "u1",
      roles: ["Owner"],
      exp: 100,
      name: "Асан",
    });
    expect(readAccessToken(token({ sub: "u2", role: ["Administrator", "Owner"], exp: 1 }))?.roles).toEqual([
      "Administrator",
      "Owner",
    ]);
  });

  it("корректно декодирует кириллицу (UTF-8 в base64url)", () => {
    expect(readAccessToken(token({ sub: "u", exp: 1, name: "Айгүл Токтогулова" }))?.name).toBe("Айгүл Токтогулова");
  });

  it("возвращает null для мусора и неполных токенов", () => {
    expect(readAccessToken(undefined)).toBeNull();
    expect(readAccessToken("not-a-jwt")).toBeNull();
    expect(readAccessToken("a.%%%.c")).toBeNull();
    expect(readAccessToken(token({ role: "Owner", exp: 1 }))).toBeNull();
  });
});

describe("isExpiring", () => {
  it("учитывает запас времени", () => {
    const claims = { sub: "u", roles: [], exp: 1_000 };
    expect(isExpiring(claims, 900_000, 30)).toBe(false);
    expect(isExpiring(claims, 975_000, 30)).toBe(true);
    expect(isExpiring(claims, 1_000_000, 0)).toBe(true);
  });
});
