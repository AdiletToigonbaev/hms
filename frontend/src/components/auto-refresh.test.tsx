import { render } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AutoRefresh, isBusy } from "./auto-refresh";

const refresh = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh }) }));

describe("автообновление", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    refresh.mockClear();
  });
  afterEach(() => {
    vi.useRealTimers();
    document.body.innerHTML = "";
  });

  it("обновляет по таймеру", () => {
    render(<AutoRefresh seconds={30} />);
    vi.advanceTimersByTime(30_000);
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it("не мешает вводу: пока фокус в поле — ждёт", () => {
    const input = document.createElement("input");
    document.body.appendChild(input);
    input.focus();
    expect(isBusy(document)).toBe(true);
    render(<AutoRefresh seconds={30} />);
    vi.advanceTimersByTime(60_000);
    expect(refresh).not.toHaveBeenCalled();
  });

  it("не обновляет при открытом окне подтверждения", () => {
    const dialog = document.createElement("div");
    dialog.setAttribute("role", "alertdialog");
    document.body.appendChild(dialog);
    expect(isBusy(document)).toBe(true);
  });
});
