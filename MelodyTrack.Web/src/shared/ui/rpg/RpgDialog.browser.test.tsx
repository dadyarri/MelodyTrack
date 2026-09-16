import { useState } from "react";
import { afterEach, describe, expect, it } from "vitest";
import { page, userEvent } from "vitest/browser";
import { render } from "vitest-browser-react";

import { RpgDialog } from "./RpgDialog";
import { RpgButton, RpgTheme } from "./RpgPrimitives";

afterEach(async () => {
  await page.viewport(1280, 800);
});

function DialogHarness({ busy = false }: { busy?: boolean }) {
  const [open, setOpen] = useState(false);
  const [pending, setPending] = useState(busy);
  return (
    <RpgTheme>
      <RpgButton
        onClick={() => {
          setOpen(true);
        }}
      >
        Открыть свиток
      </RpgButton>
      <RpgDialog
        open={open}
        title="Свиток практики"
        pending={pending}
        onRequestClose={() => {
          setOpen(false);
        }}
        footer={
          <RpgButton
            onClick={() => {
              setPending(false);
            }}
          >
            Завершить ожидание
          </RpgButton>
        }
      >
        <p>Содержание свитка</p>
      </RpgDialog>
    </RpgTheme>
  );
}

describe("RpgDialog in supported browsers", () => {
  it("contains keyboard focus and restores the trigger after Escape", async () => {
    const screen = await render(<DialogHarness />);

    await screen.getByRole("button", { name: "Открыть свиток" }).click();

    await expect.element(screen.getByRole("dialog", { name: "Свиток практики" })).toBeVisible();
    await expect.poll(() => document.activeElement?.textContent).toBe("Закрыть");
    await userEvent.keyboard("{Shift>}{Tab}{/Shift}");
    await expect.poll(() => document.activeElement?.textContent).toBe("Завершить ожидание");
    await userEvent.keyboard("{Escape}");

    await expect.poll(() => document.querySelector("dialog")?.open).toBe(false);
    await expect.poll(() => document.activeElement?.textContent).toBe("Открыть свиток");
  });

  it("preserves a pending operation when Escape or close is requested", async () => {
    const screen = await render(<DialogHarness busy />);

    await screen.getByRole("button", { name: "Открыть свиток" }).click();
    await userEvent.keyboard("{Escape}");

    await expect.element(screen.getByRole("dialog")).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Закрыть", exact: true })).toBeDisabled();

    await screen.getByRole("button", { name: "Завершить ожидание" }).click();
    await screen.getByRole("button", { name: "Закрыть", exact: true }).click();

    await expect.poll(() => document.querySelector("dialog")?.open).toBe(false);
    await expect.poll(() => document.activeElement?.textContent).toBe("Открыть свиток");
  });

  it("fits long content inside a short mobile viewport with reduced motion", async () => {
    await page.viewport(568, 320);
    const screen = await render(
      <RpgTheme>
        <RpgDialog open title="Длинный свиток" onRequestClose={() => {}}>
          <p>{"Длинная строка учебного задания. ".repeat(200)}</p>
          <RpgButton>Последнее действие</RpgButton>
        </RpgDialog>
      </RpgTheme>,
    );
    const dialog = screen.getByRole("dialog").element();

    expect(dialog.getBoundingClientRect().bottom).toBeLessThanOrEqual(320);
    expect(dialog.scrollHeight).toBeGreaterThan(dialog.clientHeight);
    expect(getComputedStyle(dialog).animationName).toBe("none");
    await screen.getByRole("button", { name: "Последнее действие" }).click();

    await expect.element(screen.getByRole("button", { name: "Последнее действие" })).toBeVisible();
  });
});
