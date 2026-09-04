import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { ListQueryStatus } from "./ListQueryStatus";

describe("ListQueryStatus", () => {
  it("keeps a failed refresh visible and retryable", () => {
    const onRetry = vi.fn();

    render(<ListQueryStatus error={new Error("Сервер недоступен")} isFetching onRetry={onRetry} />);
    fireEvent.click(screen.getByRole("button", { name: "Повторить" }));

    expect(screen.getByText("Не удалось обновить данные.")).toBeInTheDocument();
    expect(onRetry).toHaveBeenCalledOnce();
  });

  it("announces background refresh without replacing the list", () => {
    render(<ListQueryStatus isFetching />);

    expect(screen.getByRole("status")).toHaveTextContent("Обновляем данные…");
  });
});
