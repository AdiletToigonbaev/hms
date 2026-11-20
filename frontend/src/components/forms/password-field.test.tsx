import { fireEvent, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { describe, expect, it } from "vitest";
import messages from "../../../messages/ru.json";
import { PasswordField } from "./password-field";

function renderField() {
  return render(
    <NextIntlClientProvider locale="ru" messages={messages}>
      <PasswordField name="password" label="Пароль" defaultValue="secret-123" />
    </NextIntlClientProvider>,
  );
}

describe("PasswordField", () => {
  it("скрывает пароль по умолчанию и показывает по кнопке-глазику", () => {
    renderField();
    const input = screen.getByLabelText("Пароль", { selector: "input" });
    expect(input).toHaveAttribute("type", "password");

    const toggle = screen.getByRole("button", { name: "Показать пароль" });
    expect(toggle).toHaveAttribute("type", "button"); // не отправляет форму
    fireEvent.click(toggle);

    expect(input).toHaveAttribute("type", "text");
    expect(input).toHaveValue("secret-123");
    expect(screen.getByRole("button", { name: "Скрыть пароль" })).toHaveAttribute("aria-pressed", "true");

    fireEvent.click(screen.getByRole("button", { name: "Скрыть пароль" }));
    expect(input).toHaveAttribute("type", "password");
  });
});
