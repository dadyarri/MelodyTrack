import "@/app/styles/index.css";
import "@/app/styles/mobile-compatibility.css";

import { describe, expect, it, vi } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import type { DashboardStats } from "@/entities/dashboard";
import { ThemeProvider } from "@/shared/config";

import { DashboardContent } from "./DashboardPage";

const dashboard: DashboardStats = {
  personalClientsCount: 2,
  monthIncome: 3200,
  today: {
    date: "2026-07-29",
    count: 1,
    appointments: [
      {
        id: "01JTESTAPPOINTMENT0000000000",
        client: {
          id: "01JTESTCLIENT00000000000000",
          firstName: "Анна",
          lastName: "Иванова",
          contacts: { phone: "+79990000000" },
        },
        service: { id: "01JTESTSERVICE0000000000000", name: "Урок вокала" },
        startDate: "2026-07-29T10:00:00",
        endDate: "2026-07-29T11:00:00",
        status: "planned",
      },
    ],
  },
  tomorrow: {
    date: "2026-07-30",
    count: 0,
    appointments: [],
  },
  organization: {
    totalClients: 12,
    debtorsCount: 2,
    totalDebt: 2400,
    totalPositiveBalance: 1500,
    appointmentsToday: 4,
    appointmentsTomorrow: 3,
    monthIncome: 18_000,
    monthExpenses: 5_000,
    monthNet: 13_000,
  },
};

describe("dashboard overview in supported browsers", () => {
  it.each([
    { width: 1280, height: 800, label: "desktop", schedulesShareRow: true, statisticsShareRow: true },
    { width: 320, height: 568, label: "compact mobile", schedulesShareRow: false, statisticsShareRow: false },
  ])("restores the overview card layout on $label", async ({ width, height, schedulesShareRow, statisticsShareRow }) => {
    await page.viewport(width, height);
    const screen = await render(
      <ThemeProvider>
        <DashboardContent data={dashboard} isLoading={false} isError={false} onRetry={vi.fn()} canSeeOrganization />
      </ThemeProvider>,
    );

    await expect.element(screen.getByRole("heading", { name: "Обзор" })).toBeVisible();
    await expect.element(screen.getByText("Доход за месяц")).toBeVisible();
    await expect.element(screen.getByText("Иванова Анна — Урок вокала")).toBeVisible();
    await expect.element(screen.getByText("На завтра записей нет")).toBeVisible();
    await expect.element(screen.getByText("Клиенты с отрицательным балансом")).toBeVisible();
    await expect.poll(() => document.documentElement.scrollWidth - document.documentElement.clientWidth).toBeLessThanOrEqual(0);
    await expect.poll(() => hasNestedVerticalScroller()).toBe(false);
    await expect.poll(() => cardsShareRow("Записи на сегодня", "Записи на завтра")).toBe(schedulesShareRow);
    await expect.poll(() => cardsShareRow("Записи сегодня", "Общий долг")).toBe(statisticsShareRow);
  });

  it("keeps previously loaded schedule visible to a regular user when a refresh fails", async () => {
    await page.viewport(390, 844);
    const screen = await render(
      <ThemeProvider>
        <DashboardContent data={dashboard} isLoading={false} isError onRetry={vi.fn()} />
      </ThemeProvider>,
    );

    await expect.element(screen.getByText("Иванова Анна — Урок вокала")).toBeVisible();
    await expect.element(screen.getByText("На завтра записей нет")).toBeVisible();
    await expect.element(screen.getByText("Общий долг")).not.toBeInTheDocument();
    await expect.element(screen.getByText("Клиенты с отрицательным балансом")).not.toBeInTheDocument();
  });
});

function hasNestedVerticalScroller() {
  return [...document.querySelectorAll<HTMLElement>(".ant-card-body, .ant-list")].some((element) => {
    const overflowY = getComputedStyle(element).overflowY;
    return overflowY === "auto" || overflowY === "scroll";
  });
}

function cardsShareRow(firstTitle: string, secondTitle: string) {
  const cards = [...document.querySelectorAll<HTMLElement>(".ant-card")];
  const first = cards.find((card) => card.querySelector(".ant-card-head-title")?.textContent.startsWith(firstTitle));
  const second = cards.find((card) => card.querySelector(".ant-card-head-title")?.textContent.startsWith(secondTitle));
  if (!first || !second) {
    return false;
  }

  return Math.abs(first.getBoundingClientRect().top - second.getBoundingClientRect().top) <= 1;
}
