import { describe, expect, it } from "vitest";
import type { PermissionInfo } from "@/lib/api/types";
import { computeOverrides, computeRoleGrants, groupPermissions, roleChange } from "./permissions";

const p = (code: string, group = "G"): PermissionInfo => ({ code, group, description: code, ownerOnly: false });
const configurable = [p("booking.view"), p("booking.cancel"), p("finance.report.view"), p("refund.approve", "Финансы")];
const role = new Set(["booking.view", "booking.cancel"]);

describe("computeOverrides", () => {
  it("без изменений относительно роли — пусто", () => {
    expect(computeOverrides(configurable, role, new Set(role), {})).toEqual([]);
  });

  it("выдача сверх роли и отзыв права роли", () => {
    const checked = new Set(["booking.view", "finance.report.view"]);
    expect(computeOverrides(configurable, role, checked, {})).toEqual([
      { code: "booking.cancel", isGranted: false, limitAmount: null },
      { code: "finance.report.view", isGranted: true, limitAmount: null },
    ]);
  });

  it("лимит сохраняется только для прав с лимитом и только когда право отмечено", () => {
    const checked = new Set([...role, "refund.approve"]);
    expect(computeOverrides(configurable, role, checked, { "refund.approve": 5000, "booking.view": 1 })).toEqual([
      { code: "refund.approve", isGranted: true, limitAmount: 5000 },
    ]);
    expect(computeOverrides(configurable, role, new Set(role), { "refund.approve": 5000 })).toEqual([]);
  });
});

describe("groupPermissions", () => {
  it("группирует с сохранением порядка", () => {
    expect(groupPermissions(configurable).map(([g, items]) => [g, items.length])).toEqual([
      ["G", 3],
      ["Финансы", 1],
    ]);
  });
});

describe("computeRoleGrants", () => {
  it("берёт отмеченные права, лимит — только у прав с лимитом", () => {
    const checked = new Set(["booking.view", "refund.approve"]);
    expect(computeRoleGrants(configurable, checked, { "refund.approve": 300, "booking.view": 5 })).toEqual([
      { code: "booking.view", limitAmount: null },
      { code: "refund.approve", limitAmount: 300 },
    ]);
  });
});

describe("roleChange", () => {
  it("показывает отличие от стандартных прав роли", () => {
    const defaults = new Set(["booking.view", "booking.cancel"]);
    const checked = new Set(["booking.view", "refund.approve"]);
    expect(roleChange("booking.view", checked, defaults)).toBeNull();
    expect(roleChange("refund.approve", checked, defaults)).toBe("added");
    expect(roleChange("booking.cancel", checked, defaults)).toBe("removed");
  });
});
