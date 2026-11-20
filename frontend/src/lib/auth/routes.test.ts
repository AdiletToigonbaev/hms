import { describe, expect, it } from "vitest";
import { areaFor, canAccess, homeFor, safeNextPath } from "./routes";

describe("homeFor", () => {
  it("ведёт каждую роль в свой раздел", () => {
    expect(homeFor(["Owner"])).toBe("/owner");
    expect(homeFor(["Administrator"])).toBe("/admin");
    expect(homeFor(["Housekeeper"])).toBe("/hk");
    expect(homeFor(["Guest"])).toBe("/account");
    expect(homeFor([])).toBe("/account");
  });
});

describe("canAccess", () => {
  it("закрывает чужие разделы", () => {
    expect(canAccess("/owner/staff", ["Administrator"])).toBe(false);
    expect(canAccess("/owner/staff", ["Owner"])).toBe(true);
    expect(canAccess("/hk", ["Guest"])).toBe(false);
    expect(canAccess("/admin", ["Owner"])).toBe(true);
    expect(canAccess("/account", ["Housekeeper"])).toBe(true);
  });

  it("не путает разделы с похожими префиксами", () => {
    expect(areaFor("/ownership")).toBeUndefined();
    expect(areaFor("/owner")?.prefix).toBe("/owner");
    expect(canAccess("/login", [])).toBe(true);
  });
});

describe("safeNextPath", () => {
  it("пропускает только внутренние пути", () => {
    expect(safeNextPath("/owner/staff?page=2")).toBe("/owner/staff?page=2");
    expect(safeNextPath("https://evil.example")).toBeNull();
    expect(safeNextPath("//evil.example")).toBeNull();
    expect(safeNextPath("/\\evil.example")).toBeNull();
    expect(safeNextPath(null)).toBeNull();
  });
});
