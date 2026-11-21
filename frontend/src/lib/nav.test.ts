import { describe, expect, it } from "vitest";
import { housekeeperNav, staffNav } from "./nav";

const t = (key: string) => key;

describe("меню персонала", () => {
  it("администратор видит раздел «Уборка», но не разделы владельца", () => {
    const hrefs = staffNav(["Administrator"], t).map((i) => i.href);
    expect(hrefs).toContain("/admin/housekeeping");
    expect(hrefs).not.toContain("/owner/settings");
  });

  it("уборщик видит только свои задачи, историю и заявки — без финансов и гостей", () => {
    const hrefs = housekeeperNav(t).map((i) => i.href);
    expect(hrefs).toEqual(["/hk", "/hk/history", "/hk/requests"]);
  });
});
