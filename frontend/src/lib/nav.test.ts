import { describe, expect, it } from "vitest";
import { housekeeperNav, staffNav } from "./nav";

const t = (key: string) => key;

describe("меню персонала", () => {
  it("администратор видит раздел «Уборка», но не разделы владельца", () => {
    const hrefs = staffNav(["Administrator"], t).map((i) => i.href);
    expect(hrefs).toContain("/admin/housekeeping");
    expect(hrefs).not.toContain("/owner/settings");
  });

  it("«Отчёты» — владельцу и администратору с правом «Финансовые отчёты»", () => {
    expect(staffNav(["Administrator"], t).map((i) => i.href)).not.toContain("/admin/reports");
    expect(staffNav(["Administrator"], t, ["finance.report.view"]).map((i) => i.href)).toContain("/admin/reports");
    expect(staffNav(["Owner"], t).map((i) => i.href)).toContain("/admin/reports");
  });

  it("уборщик видит только свои задачи, историю и заявки — без финансов и гостей", () => {
    const hrefs = housekeeperNav(t).map((i) => i.href);
    expect(hrefs).toEqual(["/hk", "/hk/history", "/hk/requests"]);
  });
});

describe("меню «Сообщения»", () => {
  it("переписка — сотруднику с правом whatsapp.send и владельцу; шаблоны — только владельцу", () => {
    expect(staffNav(["Administrator"], t).map((i) => i.href)).not.toContain("/admin/messages");
    expect(staffNav(["Administrator"], t, ["whatsapp.send"]).map((i) => i.href)).toContain("/admin/messages");
    const owner = staffNav(["Owner"], t).map((i) => i.href);
    expect(owner).toContain("/admin/messages");
    expect(owner).toContain("/owner/messages");
    expect(staffNav(["Administrator"], t, ["whatsapp.send"]).map((i) => i.href)).not.toContain("/owner/messages");
  });
});
