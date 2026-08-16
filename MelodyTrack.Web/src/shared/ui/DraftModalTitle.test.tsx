import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { DraftModalTitle } from "./DraftModalTitle";

describe("DraftModalTitle", () => {
  it.each([
    ["loading", "Загружаем черновик…"],
    ["saving", "Сохраняем черновик…"],
    ["saved", "Черновик сохранён"],
    ["failed", "Не удалось сохранить черновик"],
  ] as const)("exposes the %s persistence state without visible text", (saveStatus, label) => {
    render(<DraftModalTitle title="Новая запись" restored={false} saveStatus={saveStatus} />);

    expect(screen.getByLabelText(label)).toBeInTheDocument();
    expect(screen.queryByText(label)).not.toBeInTheDocument();
  });

  it("makes restoration explicit after hydration completes", () => {
    render(<DraftModalTitle title="Новая запись" restored saveStatus="saved" />);

    expect(screen.getByLabelText("Черновик восстановлен")).toBeInTheDocument();
    expect(screen.queryByText("Черновик восстановлен")).not.toBeInTheDocument();
  });

  it("retries a failed save from the compact status icon", () => {
    const onRetry = vi.fn();
    render(<DraftModalTitle title="Новая запись" restored={false} saveStatus="failed" onRetry={onRetry} />);

    fireEvent.click(screen.getByRole("button", { name: "Не удалось сохранить черновик. Нажмите, чтобы повторить." }));

    expect(onRetry).toHaveBeenCalledOnce();
  });
});
