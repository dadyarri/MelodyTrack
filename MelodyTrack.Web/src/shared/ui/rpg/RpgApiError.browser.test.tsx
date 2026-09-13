import { afterEach, describe, expect, it, vi } from "vitest";
import { render } from "vitest-browser-react";

import { AppError } from "@/shared/api";

import { RpgApiError } from "./RpgApiError";
import { RpgTheme } from "./RpgPrimitives";

const originalClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");
const traceId = "00000000000000000000000000000022";

afterEach(() => {
  if (originalClipboard) Object.defineProperty(navigator, "clipboard", originalClipboard);
  else Reflect.deleteProperty(navigator, "clipboard");
});

describe("RpgApiError", () => {
  it("keeps the trace selectable and retry available after clipboard denial", async () => {
    const writeText = vi.fn().mockRejectedValue(new DOMException("Denied", "NotAllowedError"));
    const retry = vi.fn();
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    const error = new AppError("Сервис временно недоступен", {
      kind: "http",
      status: 503,
      problem: { type: "about:blank", title: "Сервис временно недоступен", status: 503, instance: "", code: "", errors: [], traceId },
    });
    const screen = await render(<RpgTheme><RpgApiError error={error} onRetry={retry} /></RpgTheme>);

    await screen.getByRole("button", { name: "Скопировать Trace ID" }).click();

    expect(writeText).toHaveBeenCalledWith(traceId);
    await expect.element(screen.getByText("Не удалось скопировать. Выделите Trace ID вручную.")).toBeVisible();
    const trace = screen.getByText(traceId, { exact: true }).element();
    expect(getComputedStyle(trace).userSelect).toBe("text");
    await screen.getByRole("button", { name: "Попробовать снова" }).click();

    expect(retry).toHaveBeenCalledOnce();
  });
});
