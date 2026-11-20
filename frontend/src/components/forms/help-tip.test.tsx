import { fireEvent, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { describe, expect, it } from "vitest";
import messages from "../../../messages/ru.json";
import { HelpTip } from "./help-tip";

describe("HelpTip", () => {
  it("по нажатию на «?» показывает пояснение, пример и отметку «пока не применяется»", async () => {
    render(
      <NextIntlClientProvider locale="ru" messages={messages}>
        <HelpTip title="Уборка после выезда" text="Номер закрыт на уборку." example="30 мин после 14:00 — с 14:30." pending="Заработает позже." />
      </NextIntlClientProvider>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Подсказка: Уборка после выезда" }));

    expect(await screen.findByText("Номер закрыт на уборку.")).toBeInTheDocument();
    expect(screen.getByText("30 мин после 14:00 — с 14:30.")).toBeInTheDocument();
    expect(screen.getByText("Заработает позже.")).toBeInTheDocument();
  });
});
