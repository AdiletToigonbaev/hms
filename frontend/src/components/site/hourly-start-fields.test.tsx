import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { afterEach, describe, expect, it } from "vitest";
import messages from "../../../messages/ru.json";
import { firstStartHour, HourlyStartFields } from "./hourly-start-fields";

const today = "2026-11-23";

afterEach(cleanup);

function renderFields(defaultDate: string, defaultHour: number) {
  return render(
    <NextIntlClientProvider locale="ru" messages={messages}>
      <HourlyStartFields defaultDate={defaultDate} defaultHour={defaultHour} today={today} nowHour={14} className="" />
    </NextIntlClientProvider>,
  );
}

const option = (label: string) => screen.getByRole("option", { name: label }) as HTMLOptionElement;

describe("HourlyStartFields", () => {
  it("сегодня — прошедшие часы недоступны, другой день — все", () => {
    expect(firstStartHour(today, today, 14)).toBe(15);
    expect(firstStartHour(today, today, 23)).toBe(23);
    expect(firstStartHour("2026-11-24", today, 14)).toBe(0);
  });

  it("выбор «Завтра» сразу открывает утренние часы", () => {
    renderFields(today, 15);
    expect(option("10:00").disabled).toBe(true);

    fireEvent.click(screen.getByRole("button", { name: /^Дата:/ }));
    fireEvent.click(screen.getByRole("button", { name: "Завтра" }));

    expect(option("10:00").disabled).toBe(false);
  });

  it("возврат на сегодня с прошедшим часом — ближайший доступный час", () => {
    renderFields("2026-11-24", 9);
    expect((screen.getByLabelText("Начало") as HTMLSelectElement).value).toBe("9");

    fireEvent.click(screen.getByRole("button", { name: /^Дата:/ }));
    fireEvent.click(screen.getByRole("button", { name: "Сегодня" }));

    expect((screen.getByLabelText("Начало") as HTMLSelectElement).value).toBe("15");
    expect(option("09:00").disabled).toBe(true);
  });
});
