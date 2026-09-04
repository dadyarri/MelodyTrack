import { afterEach, describe, expect, it, vi } from "vitest";

import { copyTextToClipboard } from "./clipboard";

const originalClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");

afterEach(() => {
  vi.restoreAllMocks();
  if (originalClipboard) {
    Object.defineProperty(navigator, "clipboard", originalClipboard);
  } else {
    Reflect.deleteProperty(navigator, "clipboard");
  }
});

describe("copyTextToClipboard", () => {
  it("starts clipboard access before returning control to the click handler", async () => {
    let callReturned = false;
    const writeText = vi.fn(() => {
      expect(callReturned).toBe(false);
      return Promise.resolve();
    });
    installClipboardMock(writeText);

    const result = copyTextToClipboard("prepared text");
    callReturned = true;

    await expect(result).resolves.toBe(true);
    expect(writeText).toHaveBeenCalledWith("prepared text");
  });

  it.each(["unavailable", "rejected", "throws", "getter-throws"] as const)("returns false when clipboard access is %s", async (failure) => {
    if (failure === "unavailable") {
      Object.defineProperty(navigator, "clipboard", { configurable: true, value: undefined });
    } else if (failure === "getter-throws") {
      Object.defineProperty(navigator, "clipboard", {
        configurable: true,
        get() {
          throw new DOMException("Denied", "SecurityError");
        },
      });
    } else {
      const writeText =
        failure === "rejected"
          ? vi.fn(() => Promise.reject(new DOMException("Denied", "NotAllowedError")))
          : vi.fn(() => {
              throw new DOMException("Denied", "NotAllowedError");
            });
      installClipboardMock(writeText);
    }

    await expect(copyTextToClipboard("prepared text")).resolves.toBe(false);
  });
});

function installClipboardMock(writeText: (value: string) => Promise<void>) {
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { writeText },
  });
}
