import { describe, expect, it } from "vitest";
import { changePasswordSchema, checkbox, createStaffSchema, fieldErrors, loginSchema, registerSchema } from "./auth";

describe("registerSchema", () => {
  const valid = {
    fullName: "Асан Усенов",
    email: "asan@mail.kg",
    phone: "",
    password: "long-enough-1",
    acceptPersonalData: true,
    acceptWhatsApp: false,
  };

  it("принимает корректные данные, пустой телефон становится undefined", () => {
    const parsed = registerSchema.parse(valid);
    expect(parsed.phone).toBeUndefined();
  });

  it("требует согласие на обработку ПД и пароль не короче 10 символов", () => {
    const result = registerSchema.safeParse({ ...valid, acceptPersonalData: false, password: "short" });
    expect(result.success).toBe(false);
    const errors = fieldErrors(result.error!);
    expect(errors.acceptPersonalData).toMatch(/согласие/);
    expect(errors.password).toMatch(/не менее 10/);
  });
});

describe("createStaffSchema", () => {
  it("требует email или телефон", () => {
    const result = createStaffSchema.safeParse({
      fullName: "Айжан",
      email: "",
      phone: "",
      role: "Housekeeper",
      initialPassword: "temp-password-1",
    });
    expect(result.success).toBe(false);
    expect(fieldErrors(result.error!).email).toMatch(/email или телефон/);
  });

  it("не позволяет создать владельца или гостя", () => {
    const result = createStaffSchema.safeParse({
      fullName: "Х",
      email: "x@x.kg",
      phone: "",
      role: "Owner",
      initialPassword: "temp-password-1",
    });
    expect(result.success).toBe(false);
  });
});

describe("changePasswordSchema", () => {
  it("новый пароль должен отличаться", () => {
    const result = changePasswordSchema.safeParse({ currentPassword: "same-password-1", newPassword: "same-password-1" });
    expect(fieldErrors(result.error!).newPassword).toMatch(/отличаться/);
  });
});

describe("checkbox", () => {
  it("распознаёт значение чекбокса из формы", () => {
    const form = new FormData();
    form.set("a", "on");
    expect(checkbox(form, "a")).toBe(true);
    expect(checkbox(form, "b")).toBe(false);
  });
});

describe("локаль zod", () => {
  it("сообщения по умолчанию — на русском", () => {
    const result = loginSchema.safeParse({ login: null, password: null });
    expect(result.success).toBe(false);
    expect(Object.values(fieldErrors(result.error!)).join(" ")).toMatch(/[а-яё]/i);
  });
});
