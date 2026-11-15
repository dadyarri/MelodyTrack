import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AppErrorBoundary } from "./AppErrorBoundary";

function BrokenScreen(): never {
  throw new Error("synthetic private failure");
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("application error recovery", () => {
  it("renders the working screen", () => {
    render(
      <AppErrorBoundary>
        <p>Рабочий экран</p>
      </AppErrorBoundary>,
    );
    expect(screen.getByText("Рабочий экран")).toBeTruthy();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("replaces a crashed screen with a safe recovery action", () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    render(
      <AppErrorBoundary>
        <BrokenScreen />
      </AppErrorBoundary>,
    );
    expect(screen.getByRole("alert").textContent).toContain("Не удалось открыть экран");
    expect(screen.queryByText("synthetic private failure")).toBeNull();
    expect(screen.getByRole("button", { name: "Обновить страницу" })).toBeTruthy();
  });
});
