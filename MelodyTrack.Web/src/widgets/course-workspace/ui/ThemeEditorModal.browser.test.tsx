import { Modal } from "antd";
import { describe, expect, it } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import styles from "./CourseWorkspace.module.css";

describe("Course theme editor modal", () => {
  it("fills the visual viewport", async () => {
    await page.viewport(390, 640);
    const screen = await render(
      <Modal
        open
        title="Редактор темы"
        footer={null}
        width="100vw"
        rootClassName={styles.themeEditorModalRoot}
        className={styles.themeEditorModal}
      >
        Содержимое редактора
      </Modal>,
    );
    const bounds = screen.getByRole("dialog", { name: "Редактор темы" }).element().getBoundingClientRect();

    expect(bounds.left).toBe(0);
    expect(bounds.top).toBe(0);
    expect(bounds.width).toBe(390);
    expect(bounds.height).toBe(640);
  });

  it("scrolls through the form from its central content area", async () => {
    await page.viewport(1280, 800);
    const screen = await render(
      <Modal
        open
        title="Редактор темы"
        footer={null}
        width="100vw"
        rootClassName={styles.themeEditorModalRoot}
        className={styles.themeEditorModal}
      >
        <div style={{ height: 1600, background: "transparent" }}>Длинная форма</div>
      </Modal>,
    );
    const dialog = screen.getByRole("dialog", { name: "Редактор темы" }).element();
    const body = dialog.querySelector<HTMLElement>(".ant-modal-body");

    expect(body).not.toBeNull();
    if (!body) throw new Error("Modal body was not rendered");
    expect(getComputedStyle(body).overflowY).toBe("auto");
    expect(body.scrollHeight).toBeGreaterThan(body.clientHeight);
    body.scrollTop = 200;

    expect(body.scrollTop).toBe(200);
  });
});
