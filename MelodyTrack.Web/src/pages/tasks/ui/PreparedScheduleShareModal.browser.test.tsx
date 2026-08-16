import "@/app/styles/index.css";
import "@/app/styles/mobile-compatibility.css";

import { App as AntdApp } from "antd";
import { afterEach, describe, expect, it, vi } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import { ThemeProvider } from "@/shared/config";

import { PreparedScheduleShareModal } from "./PreparedScheduleShareModal";

const originalClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");
const originalClipboardItem = Object.getOwnPropertyDescriptor(globalThis, "ClipboardItem");

afterEach(() => {
  vi.restoreAllMocks();
  restoreProperty(navigator, "clipboard", originalClipboard);
  restoreProperty(globalThis, "ClipboardItem", originalClipboardItem);
});

describe("prepared schedule sharing in supported browsers", () => {
  it("copies only from the explicit gesture and then reveals the messenger action", async () => {
    await page.viewport(390, 664);
    const write = vi.fn(() => Promise.resolve());
    installImageClipboard(write);
    const blob = new Blob(["png"], { type: "image/png" });
    const screen = await render(
      <ThemeProvider>
        <AntdApp>
          <PreparedScheduleShareModal
            content={{ blob, fileName: "schedule.png", messengerUrl: "https://vk.me/teacher" }}
            onClose={vi.fn()}
          />
        </AntdApp>
      </ThemeProvider>,
    );

    expect(write).not.toHaveBeenCalled();
    await expect.element(screen.getByRole("dialog", { name: "Расписание готово" })).toBeVisible();
    await expect.element(screen.getByRole("link", { name: "Открыть VK" })).not.toBeInTheDocument();

    await screen.getByRole("button", { name: "Скопировать изображение" }).click();

    expect(write).toHaveBeenCalledOnce();
    await expect.element(screen.getByText("Расписание скопировано. Теперь можно открыть мессенджер.")).toBeVisible();
    await expect.element(screen.getByRole("link", { name: "Открыть VK" })).toHaveAttribute("href", "https://vk.me/teacher");
    await expect.poll(() => document.documentElement.scrollWidth - document.documentElement.clientWidth).toBeLessThanOrEqual(0);
  });

  it("keeps the download fallback when image clipboard access is rejected", async () => {
    await page.viewport(320, 568);
    installImageClipboard(vi.fn(() => Promise.reject(new DOMException("Denied", "NotAllowedError"))));
    const screen = await render(
      <ThemeProvider>
        <AntdApp>
          <PreparedScheduleShareModal
            content={{
              blob: new Blob(["png"], { type: "image/png" }),
              fileName: "schedule.png",
              messengerUrl: "tg://resolve?domain=teacher",
            }}
            onClose={vi.fn()}
          />
        </AntdApp>
      </ThemeProvider>,
    );

    await screen.getByRole("button", { name: "Скопировать изображение" }).click();

    await expect.element(screen.getByText(/Скачайте PNG и прикрепите его вручную/)).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Скачать PNG" })).toBeVisible();
    await expect.element(screen.getByRole("link", { name: "Открыть Telegram" })).not.toBeInTheDocument();
  });
});

function installImageClipboard(write: (items: ClipboardItems) => Promise<void>) {
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { write },
  });
  Object.defineProperty(globalThis, "ClipboardItem", {
    configurable: true,
    value: class ClipboardItemMock {
      constructor(readonly items: Record<string, Blob>) {}
    },
  });
}

function restoreProperty(target: object, name: PropertyKey, descriptor?: PropertyDescriptor) {
  if (descriptor) {
    Object.defineProperty(target, name, descriptor);
  } else {
    Reflect.deleteProperty(target, name);
  }
}
