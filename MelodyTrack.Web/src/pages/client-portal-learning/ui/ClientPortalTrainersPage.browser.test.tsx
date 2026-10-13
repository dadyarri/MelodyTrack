import { afterEach, describe, expect, it } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import { ClientPortalTrainersPage } from "./ClientPortalLearningPages";

afterEach(async () => {
  await page.viewport(1280, 900);
});

describe("portal independent practice", () => {
  it("offers editable trainer settings outside course assignments", async () => {
    await page.viewport(1280, 900);
    const screen = await render(<ClientPortalTrainersPage />);

    await expect.element(screen.getByRole("spinbutton", { name: "Темп, BPM", exact: true })).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Добавить такт" })).toBeVisible();
    await expect.element(screen.getByText(/Эти настройки не изменяют задания преподавателя/)).toBeVisible();
  });
  it("shows the unsupported-screen message on a narrow portal viewport", async () => {
    await page.viewport(390, 844);
    const screen = await render(<ClientPortalTrainersPage />);

    await expect.element(screen.getByText("Тренажёр пока не поддерживает небольшие экраны")).toBeVisible();
    expect(document.querySelector("canvas")).toBeNull();
  });
});
