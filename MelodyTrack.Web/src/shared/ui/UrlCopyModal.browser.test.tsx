import "@/app/styles/index.css";
import "@/app/styles/mobile-compatibility.css";

import { App as AntdApp, Button } from "antd";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { page, userEvent } from "vitest/browser";
import { render } from "vitest-browser-react";

import { ThemeProvider } from "@/shared/config";

import { UrlCopyModal } from "./UrlCopyModal";
import { useUrlCopyModal } from "./useUrlCopyModal";

const generatedUrl = `https://example.test/portal/access?token=${"long-secret-token-".repeat(24)}`;
const originalClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");

afterEach(() => {
  vi.restoreAllMocks();
  if (originalClipboard) {
    Object.defineProperty(navigator, "clipboard", originalClipboard);
  } else {
    Reflect.deleteProperty(navigator, "clipboard");
  }
});

describe("URL copy modal in supported browsers", () => {
  it.each([
    { width: 1280, height: 800, label: "desktop" },
    { width: 320, height: 568, label: "compact mobile" },
  ])("separates delayed generation from the $label copy gesture", async ({ width, height }) => {
    await page.viewport(width, height);
    const writeText = installClipboardMock().mockResolvedValue();
    const screen = await render(<UrlCopyHarness />);

    await screen.getByRole("button", { name: "Создать ссылку с задержкой" }).click();
    expect(writeText).not.toHaveBeenCalled();
    await expect.element(screen.getByRole("dialog", { name: "Ссылка клиентского кабинета" })).toBeVisible();
    expect(writeText).not.toHaveBeenCalled();

    const copyButton = screen.getByRole("button", { name: "Скопировать ссылку" });
    await expect.poll(() => document.activeElement?.textContent).toContain("Скопировать ссылку");
    await copyButton.click();

    expect(writeText).toHaveBeenCalledWith(generatedUrl);
    await expect.element(screen.getByText("Ссылка скопирована")).toBeVisible();
    await userEvent.keyboard("{Enter}");
    expect(writeText).toHaveBeenCalledTimes(2);
    await assertNoDocumentOverflow();
  });

  it("keeps a long URL selectable on compact screens after clipboard rejection", async () => {
    await page.viewport(320, 568);
    installClipboardMock().mockRejectedValue(new DOMException("Permission denied", "NotAllowedError"));
    const generate = vi.fn();
    const screen = await render(<UrlCopyHarness immediate onGenerate={generate} />);

    await screen.getByRole("button", { name: "Создать ссылку" }).click();
    await screen.getByRole("button", { name: "Скопировать ссылку" }).click();

    await expect.element(screen.getByText(/скопируйте её вручную/)).toBeVisible();
    await expect.element(screen.getByLabelText("Ссылка", { exact: true })).toHaveValue(generatedUrl);
    await screen.getByLabelText("Ссылка", { exact: true }).click();
    expect(generate).toHaveBeenCalledOnce();
    await assertNoDocumentOverflow();
  });

  it("does not reopen a stale URL when a later generation request fails", async () => {
    await page.viewport(390, 664);
    const writeText = installClipboardMock().mockResolvedValue();
    const generate = vi.fn();
    const screen = await render(<UrlCopyHarness immediate onGenerate={generate} />);

    await screen.getByRole("button", { name: "Создать ссылку", exact: true }).click();
    await expect.element(screen.getByRole("dialog")).toBeVisible();
    await screen.getByRole("button", { name: "Закрыть" }).click();
    await screen.getByRole("button", { name: "Сымитировать ошибку API" }).click();

    await expect.poll(() => document.querySelector('[role="dialog"]')).toBeNull();
    expect(generate).toHaveBeenCalledTimes(2);
    expect(writeText).not.toHaveBeenCalled();
  });
});

function installClipboardMock() {
  const writeText = vi.fn<(value: string) => Promise<void>>();
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { writeText },
  });
  return writeText;
}

function UrlCopyHarness({ immediate = false, onGenerate }: { immediate?: boolean; onGenerate?: () => void }) {
  const modal = useUrlCopyModal("browser-user");
  const [pending, setPending] = useState(false);

  const generateUrl = (succeeds = true) => {
    onGenerate?.();
    setPending(true);
    const generation = succeeds
      ? immediate
        ? Promise.resolve(generatedUrl)
        : new Promise<string>((resolve) => {
            setTimeout(() => {
              resolve(generatedUrl);
            }, 80);
          })
      : Promise.reject(new Error("API failure"));
    void generation.then(
      (url) => {
        setPending(false);
        modal.openUrlModal({
          url,
          title: "Ссылка клиентского кабинета",
          warning: "Предыдущая ссылка отключена.",
        });
      },
      () => {
        setPending(false);
      },
    );
  };

  return (
    <ThemeProvider>
      <AntdApp>
        <main style={{ padding: 12 }}>
          <Button
            loading={pending}
            onClick={() => {
              generateUrl();
            }}
          >
            {immediate ? "Создать ссылку" : "Создать ссылку с задержкой"}
          </Button>
          <Button
            onClick={() => {
              generateUrl(false);
            }}
          >
            Сымитировать ошибку API
          </Button>
          <UrlCopyModal {...modal.urlModalProps} />
        </main>
      </AntdApp>
    </ThemeProvider>
  );
}

async function assertNoDocumentOverflow() {
  await expect.poll(() => document.documentElement.scrollWidth - document.documentElement.clientWidth).toBeLessThanOrEqual(0);
}
