import { useQueryClient } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { QueryProvider } from "./query-provider";

function Probe() {
  const client = useQueryClient();
  return <span>{client.getDefaultOptions().queries?.retry === 1 ? "ok" : "bad"}</span>;
}

describe("QueryProvider", () => {
  it("предоставляет QueryClient с настройками по умолчанию", () => {
    render(
      <QueryProvider>
        <Probe />
      </QueryProvider>,
    );
    expect(screen.getByText("ok")).toBeInTheDocument();
  });
});
