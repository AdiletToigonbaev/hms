import { fireEvent, render, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CrashScreen, errorDigest } from "./crash-screen";

const text = { title: "Что-то пошло не так", hint: "Попробуйте ещё раз.", retry: "Повторить", home: "На главную" };

describe("CrashScreen", () => {
  it("«Повторить» вызывает retry, «На главную» ведёт на /", () => {
    const onRetry = vi.fn();
    const { container } = render(<CrashScreen text={text} onRetry={onRetry} />);
    const view = within(container);

    fireEvent.click(view.getByRole("button", { name: "Повторить" }));

    expect(onRetry).toHaveBeenCalledOnce();
    expect(view.getByRole("link", { name: "На главную" })).toHaveAttribute("href", "/");
    expect(view.getByRole("alert")).toHaveTextContent("Что-то пошло не так");
  });

  it("код ошибки показывается, только если он есть", () => {
    const { container } = render(<CrashScreen text={{ ...text, code: "Код ошибки: 123" }} onRetry={() => {}} />);
    expect(within(container).getByText("Код ошибки: 123")).toBeInTheDocument();
  });
});

describe("errorDigest", () => {
  it("берёт digest ошибки Server Component, иначе — ничего", () => {
    expect(errorDigest(Object.assign(new Error("x"), { digest: "abc" }))).toBe("abc");
    expect(errorDigest(new Error("x"))).toBeUndefined();
    expect(errorDigest("строка")).toBeUndefined();
  });
});
