import { describe, expect, it } from "vitest";
import { fieldErrors } from "./auth";
import { roomSchema, roomTypeSchema, statusChangeSchema } from "./rooms";

const typeId = "01a1f8c0-0f84-730e-b19a-f1a495032489";

describe("roomTypeSchema", () => {
  it("принимает цену с запятой и пробелами, приводит числа", () => {
    const parsed = roomTypeSchema.parse({ name: "Стандарт", capacity: "2", bedsCount: "1", bedType: "Double", basePrice: "3 500,50", sortOrder: "" });
    expect(parsed).toMatchObject({ capacity: 2, bedsCount: 1, basePrice: 3500.5 });
  });

  it("отклоняет третий знак после запятой, ноль и чужой тип кровати", () => {
    const result = roomTypeSchema.safeParse({ name: "", capacity: "0", bedsCount: "1", bedType: "Hammock", basePrice: "10,005" });
    const errors = fieldErrors(result.error!);
    expect(Object.keys(errors)).toEqual(expect.arrayContaining(["name", "capacity", "bedType", "basePrice"]));
  });
});

describe("roomSchema", () => {
  it("пустые отличия от категории — undefined (наследуются)", () => {
    const parsed = roomSchema.parse({ roomTypeId: typeId, number: "101", floor: "", capacity: "", bedsCount: "", bedType: "", priceOverride: "", description: "" });
    expect(parsed).toMatchObject({ number: "101", floor: undefined, capacity: undefined, bedType: undefined, priceOverride: undefined, description: null });
  });

  it("номер — только буквы, цифры и дефис", () => {
    const result = roomSchema.safeParse({ roomTypeId: typeId, number: "1 01; DROP", floor: "", capacity: "", bedsCount: "", bedType: "", priceOverride: "" });
    expect(fieldErrors(result.error!).number).toMatch(/цифры, буквы/);
    expect(roomSchema.safeParse({ roomTypeId: typeId, number: "2А-1", floor: "2", capacity: "", bedsCount: "", bedType: "", priceOverride: "4200" }).success).toBe(true);
  });
});

describe("statusChangeSchema", () => {
  it("вручную нельзя выбрать «занят»; причина обязательна", () => {
    expect(statusChangeSchema.safeParse({ status: "Occupied", reason: "x" }).success).toBe(false);
    expect(fieldErrors(statusChangeSchema.safeParse({ status: "Maintenance", reason: " " }).error!).reason).toMatch(/причину/);
  });
});

describe("roomTypeSchema — уборка категории", () => {
  const base = { name: "Люкс", capacity: "2", bedsCount: "1", bedType: "King", basePrice: "9000" };

  it("пустые поля — как в настройках гостиницы", () => {
    const r = roomTypeSchema.safeParse({ ...base, cleaningHourlyMinutes: "", cleaningNightlyMinutes: "" });
    expect(r.success && [r.data.cleaningHourlyMinutes, r.data.cleaningNightlyMinutes]).toEqual([undefined, undefined]);
  });

  it("своё время уборки — от 0 до 240 минут", () => {
    const ok = roomTypeSchema.safeParse({ ...base, cleaningHourlyMinutes: "45", cleaningNightlyMinutes: "90" });
    expect(ok.success && ok.data.cleaningNightlyMinutes).toBe(90);
    expect(roomTypeSchema.safeParse({ ...base, cleaningNightlyMinutes: "300" }).success).toBe(false);
  });
});
