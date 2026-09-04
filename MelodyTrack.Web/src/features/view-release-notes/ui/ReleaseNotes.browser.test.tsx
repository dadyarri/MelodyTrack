import "../../../app/styles/index.css";
import "../../../app/styles/mobile-compatibility.css";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { App as AntdApp } from "antd";
import { afterEach, describe, expect, it } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import { type ReleaseHistory, releaseQueryKeys } from "@/entities/release";

import { getReleaseNotesSeenKey } from "../model/releaseNotesStorage";
import { useReleaseNotesController } from "../model/useReleaseNotesController";
import { ReleaseNotesModal } from "./ReleaseNotes";

const history: ReleaseHistory = {
  currentVersion: "2026.07.1.1",
  releases: [
    {
      version: "2026.07.1.1",
      codename: "Accordatura",
      date: "2026-07-29",
      parentVersion: "2026.07.1",
      changes: {
        new: ["Описание обновления"],
        improved: [],
        fixed: Array.from({ length: 20 }, (_, index) => `Исправление ${String(index + 1)}`),
        security: [],
      },
    },
  ],
  page: 1,
  pageSize: 2,
  totalCount: 1,
  totalPages: 1,
  hasNextPage: false,
};

afterEach(() => {
  localStorage.clear();
});

describe("automatic release notes in supported browsers", () => {
  it.each([
    { width: 1280, height: 800, label: "desktop" },
    { width: 320, height: 568, label: "mobile" },
  ])("keeps the $label current-release modal actionable", async ({ width, height }) => {
    await page.viewport(width, height);
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    queryClient.setQueryData(releaseQueryKeys.history(), history);
    const screen = await render(
      <QueryClientProvider client={queryClient}>
        <AntdApp>
          <ReleaseNotesExperience />
        </AntdApp>
      </QueryClientProvider>,
    );

    await expect.element(screen.getByText("Исправление 2026.07.1.1 — Accordatura")).toBeVisible();
    const dismissButton = screen.getByRole("button", { name: "Понятно" });
    await expect.element(dismissButton).toBeVisible();
    await expect.poll(() => isModalBodyScrollable()).toBe(true);
    await expect.poll(() => isModalCardScrollable()).toBe(false);
    await expect.poll(() => getModalContentEndPadding()).toBeGreaterThanOrEqual(16);
    await dismissButton.click();

    await expect
      .poll(() => {
        const dialog = document.querySelector<HTMLElement>('[role="dialog"]');
        return dialog ? getComputedStyle(dialog).display : "none";
      })
      .toBe("none");
    expect(localStorage.getItem(getReleaseNotesSeenKey("browser-user"))).toBe("2026.07.1.1");
    await expect.poll(() => document.documentElement.scrollWidth - document.documentElement.clientWidth).toBeLessThanOrEqual(0);
  });
});

function ReleaseNotesExperience() {
  const controller = useReleaseNotesController({ userId: "browser-user", automaticEnabled: true });
  return <ReleaseNotesModal open={controller.open} automaticReleases={controller.automaticReleases} onClose={controller.close} />;
}

function isModalBodyScrollable() {
  const body = document.querySelector<HTMLElement>(".ant-modal-body");
  if (!body) {
    return false;
  }

  return getComputedStyle(body).overflowY === "auto" && body.scrollHeight > body.clientHeight;
}

function isModalCardScrollable() {
  const card = document.querySelector<HTMLElement>(".ant-modal-content");
  if (!card) {
    return false;
  }

  return ["auto", "scroll"].includes(getComputedStyle(card).overflowY);
}

function getModalContentEndPadding() {
  const content = document.querySelector<HTMLElement>("[data-release-notes-content]");
  return content ? Number.parseFloat(getComputedStyle(content).paddingInlineEnd) : 0;
}
