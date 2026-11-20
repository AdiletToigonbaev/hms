import { describe, expect, it } from "vitest";
import ru from "../../../messages/ru.json";
import { echoValues, errorKey, toFormState, withValues } from "./problem";

describe("echoValues", () => {
  it("возвращает только перечисленные поля — пароль не попадает в ответ", () => {
    const form = new FormData();
    form.set("email", "a@b.kg");
    form.set("password", "secret-password");
    form.set("acceptPersonalData", "");
    expect(echoValues(form, ["email", "acceptPersonalData", "missing"])).toEqual({ email: "a@b.kg", acceptPersonalData: "" });
    expect(withValues({ ok: false }, form, ["email"]).values).toEqual({ email: "a@b.kg" });
  });
});

describe("toFormState", () => {
  it("переносит ошибки полей и известный код", () => {
    expect(toFormState({ status: 400, code: "validation.failed", errors: { email: ["Некорректный email.", "x"] } })).toEqual({
      ok: false,
      error: "validation.failed",
      message: undefined,
      fieldErrors: { email: "Некорректный email." },
    });
  });

  it("код передаётся как есть; текст API сохраняется как запасной вариант", () => {
    expect(toFormState({ status: 500, code: "weird.code", title: "Сбой" })).toMatchObject({ error: "weird.code", message: "Сбой" });
    expect(toFormState({ status: 502 }).error).toBe("unknown");
    expect(toFormState({ status: 400, code: "auth.password_rejected", title: "Пароль слишком простой." }).message).toBe(
      "Пароль слишком простой.",
    );
  });

  it("у каждого известного кода ошибки есть перевод", () => {
    const codes = [
      "auth.invalid_credentials",
      "auth.locked_out",
      "auth.email_not_confirmed",
      "auth.invalid_token",
      "auth.email_taken",
      "auth.phone_taken",
      "auth.wrong_password",
      "staff.cannot_modify_owner",
      "staff.permission_not_configurable",
      "rate_limited",
      "api.unavailable",
      "room.number_taken",
      "room.has_history",
      "room.invalid_status_transition",
      "room_type.has_active_rooms",
      "file.unsupported_type",
      "concurrency.conflict",
      "integration.not_available",
      "integration.key_not_allowed",
    ];
    for (const code of codes) {
      expect(ru.Errors, code).toHaveProperty(errorKey(code));
    }
  });
});
