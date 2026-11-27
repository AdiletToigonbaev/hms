import { render, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Brand, brandLetter } from "./brand";

describe("brandLetter", () => {
  it("первая буква названия заглавной — та же в шапке, иконке и картинке для ссылок", () => {
    expect(brandLetter("Great Hotel")).toBe("G");
    expect(brandLetter("  өрнөк сарай")).toBe("Ө");
  });

  it("без названия — нейтральная «Г»", () => {
    expect(brandLetter("")).toBe("Г");
    expect(brandLetter("   ")).toBe("Г");
  });
});

describe("Brand", () => {
  // Проверки — внутри своего контейнера: автоочистки DOM между тестами в проекте нет.
  it("показывает название из настроек рядом с монограммой", () => {
    const { container } = render(<Brand name="Great Hotel" />);
    expect(within(container).getByText("Great Hotel")).toBeInTheDocument();
    expect(within(container).getByText("G")).toBeInTheDocument();
  });

  it("компактный вид — только монограмма", () => {
    const { container } = render(<Brand name="Great Hotel" compact />);
    expect(within(container).queryByText("Great Hotel")).not.toBeInTheDocument();
    expect(within(container).getByText("G")).toBeInTheDocument();
  });
});
