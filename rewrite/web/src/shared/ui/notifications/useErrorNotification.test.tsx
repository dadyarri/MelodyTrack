import { act, renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useErrorNotification } from "./useErrorNotification";

const notifications = vi.hoisted(() => ({ error: vi.fn(), dismiss: vi.fn() }));
vi.mock("sonner", () => ({ toast: notifications }));

describe("error notifications", () => {
  it("keeps one notification across renders and retries with the current action", () => {
    const originalRetry = vi.fn();
    const currentRetry = vi.fn();
    const { rerender } = renderHook(
      ({ retry }) => useErrorNotification("Сервис недоступен.", { retry }),
      { initialProps: { retry: originalRetry } },
    );

    rerender({ retry: currentRetry });
    const options = notifications.error.mock.calls[0]?.[1];
    act(() => options.action.onClick());

    expect(notifications.error).toHaveBeenCalledTimes(1);
    expect(currentRetry).toHaveBeenCalledExactlyOnceWith();
    expect(originalRetry).not.toHaveBeenCalled();
    expect(options.duration).toBe(Number.POSITIVE_INFINITY);
  });

  it("removes a resolved error and shows a subsequent failure", () => {
    const { rerender, unmount } = renderHook(({ error }) => useErrorNotification(error), {
      initialProps: { error: "Сервис недоступен." as string | null },
    });
    const id = notifications.error.mock.calls[0]?.[1].id;

    rerender({ error: null });

    expect(notifications.dismiss).toHaveBeenCalledWith(id);

    rerender({ error: "Сервис недоступен." });
    unmount();

    expect(notifications.error).toHaveBeenCalledTimes(2);
    expect(notifications.dismiss).toHaveBeenCalledTimes(2);
  });

  it("removes the prior notification when a different failure replaces it", () => {
    const { rerender } = renderHook(({ error }) => useErrorNotification(error), {
      initialProps: { error: "Не удалось загрузить список." },
    });
    const id = notifications.error.mock.calls[0]?.[1].id;

    rerender({ error: "Нет доступа." });

    expect(notifications.dismiss).toHaveBeenCalledWith(id);
    expect(notifications.error).toHaveBeenLastCalledWith(
      "Нет доступа.",
      expect.objectContaining({ id }),
    );
  });
});
