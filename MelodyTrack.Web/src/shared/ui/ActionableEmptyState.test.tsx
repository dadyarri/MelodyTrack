import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { ActionableEmptyState } from "./ActionableEmptyState";

describe("ActionableEmptyState", () => {
  it("explains the empty result and invokes its permitted action", () => {
    const onAction = vi.fn();

    render(<ActionableEmptyState description="Клиентов пока нет" actionLabel="Добавить клиента" onAction={onAction} />);
    fireEvent.click(screen.getByRole("button", { name: "Добавить клиента" }));

    expect(screen.getByText("Клиентов пока нет")).toBeInTheDocument();
    expect(onAction).toHaveBeenCalledOnce();
  });

  it("renders explanation without an unavailable action", () => {
    render(<ActionableEmptyState description="Действий не найдено" />);

    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
});
