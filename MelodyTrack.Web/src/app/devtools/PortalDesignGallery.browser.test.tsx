import "../styles/index.css";
import "../styles/mobile-compatibility.css";

import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import { PortalDesignGallery } from "./PortalDesignGallery";

const originalTheme = document.documentElement.dataset.theme;
const appearanceKey = "melodytrack.portalAppearance";
const originalAppearance = localStorage.getItem(appearanceKey);

beforeEach(() => {
  localStorage.removeItem(appearanceKey);
});

afterEach(async () => {
  if (originalTheme === undefined) delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = originalTheme;
  if (originalAppearance === null) localStorage.removeItem(appearanceKey);
  else localStorage.setItem(appearanceKey, originalAppearance);
  await page.viewport(1280, 800);
});

describe("Portal design gallery", () => {
  it("restores minimal styling across content and dialogs while retaining experience", async () => {
    localStorage.setItem(appearanceKey, "minimal");
    const screen = await render(<PortalDesignGallery />);

    await expect.element(screen.getByRole("radio", { name: "Минималистичное" })).toBeChecked();
    await expect.element(screen.getByText("Уровень 3 · 420 / 1000 опыта")).toBeVisible();
    await screen.getByRole("button", { name: "Открыть свиток" }).click();

    const dialog = screen.getByRole("dialog").element();
    expect(getComputedStyle(dialog).colorScheme).toBe("dark");
    expect(getComputedStyle(dialog).backgroundImage).toBe("none");
    expect(getComputedStyle(dialog).animationName).toBe("none");
  });
  it.each([
    { width: 320, height: 568 },
    { width: 390, height: 844 },
    { width: 1280, height: 900 },
  ])("keeps gallery controls usable at $width pixels", async ({ width, height }) => {
    await page.viewport(width, height);
    const screen = await render(<PortalDesignGallery />);

    await expect.element(screen.getByTitle("Минималистичное оформление", { exact: true })).toBeVisible();

    expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(document.documentElement.clientWidth);
  });

  it("persists the appearance selected through the custom control", async () => {
    const screen = await render(<PortalDesignGallery />);

    await screen.getByTitle("Минималистичное оформление", { exact: true }).click();

    await expect.element(screen.getByRole("radio", { name: "Минималистичное" })).toBeChecked();
    expect(localStorage.getItem(appearanceKey)).toBe("minimal");

    await screen.getByTitle("RPG-оформление", { exact: true }).click();

    await expect.element(screen.getByRole("radio", { name: "RPG", exact: true })).toBeChecked();
    expect(localStorage.getItem(appearanceKey)).toBe("rpg");
  });

  it("shows persistent completion feedback after finishing the example", async () => {
    const screen = await render(<PortalDesignGallery />);

    await screen.getByRole("button", { name: "Завершить пример" }).click();

    await expect.element(screen.getByText("Хорошая работа, музыкант")).toBeVisible();
  });

  it("replaces the error with recovery feedback after retry", async () => {
    const screen = await render(<PortalDesignGallery />);

    await screen.getByRole("button", { name: "Попробовать снова" }).click();

    await expect.element(screen.getByText("Свиток снова доступен")).toBeVisible();
  });

  it("keeps its fixed palette when the administrative theme changes", async () => {
    const screen = await render(<PortalDesignGallery />);
    const main = screen.getByRole("main").element();
    const before = getComputedStyle(main).color;

    document.documentElement.dataset.theme = "dark";

    expect(getComputedStyle(main).color).toBe(before);
    await screen.getByRole("button", { name: "Открыть свиток" }).click();
    const dialog = screen.getByRole("dialog").element();
    expect(getComputedStyle(dialog).color).toBe(before);
    expect(getComputedStyle(dialog).colorScheme).toBe("dark");
  });
});
