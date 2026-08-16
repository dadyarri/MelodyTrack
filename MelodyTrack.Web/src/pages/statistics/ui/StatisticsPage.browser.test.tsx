import "@/app/styles/index.css";
import "@/app/styles/mobile-compatibility.css";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import { dashboardApi, type FinanceReport } from "@/entities/dashboard";
import { AuthContext, type AuthContextValue } from "@/entities/session";
import { ThemeProvider } from "@/shared/config";

import { StatisticsFinancePage, StatisticsWorkPage } from "./StatisticsPage";

afterEach(() => {
  vi.restoreAllMocks();
});

describe("statistics reports in supported browsers", () => {
  it("keeps URL filters and explains unavailable teacher-level finance data", async () => {
    await page.viewport(320, 568);
    const finance = vi.spyOn(dashboardApi, "finance").mockResolvedValue(emptyFinanceReport);
    const screen = await renderStatistics(
      "/statistics/finance?start=2026-07-01&end=2026-07-31&timezone=UTC&providerId=user-1&groupBy=week",
    );

    await expect.element(screen.getByText("Фактические платежи")).toBeVisible();
    await expect.element(screen.getByText(/нельзя достоверно связать/)).toBeVisible();
    expect(finance).toHaveBeenCalledWith(
      expect.objectContaining({
        start: "2026-07-01",
        end: "2026-07-31",
        timezone: "UTC",
        providerId: "user-1",
        groupBy: "week",
      }),
    );
    await expect.poll(() => document.documentElement.scrollWidth - document.documentElement.clientWidth).toBeLessThanOrEqual(0);
    for (const tableContainer of document.querySelectorAll<HTMLElement>(".ant-table-container")) {
      expect(["", "visible", "clip"].includes(getComputedStyle(tableContainer).overflowY)).toBe(true);
    }
  });

  it("shows a recoverable report error without losing the page", async () => {
    vi.spyOn(dashboardApi, "work").mockRejectedValue(new Error("offline"));
    const screen = await renderStatistics();

    await expect.element(screen.getByText("Не удалось загрузить статистику")).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Повторить" })).toBeVisible();
  });
});

async function renderStatistics(
  initialEntry = "/statistics/work?start=2026-07-01&end=2026-07-31&timezone=UTC&providerId=user-1&groupBy=week",
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const auth: AuthContextValue = {
    isLoading: false,
    isAuthenticated: true,
    user: {
      id: "user-1",
      email: "teacher@example.test",
      firstName: "Анна",
      lastName: "Иванова",
      roleDisplayName: "Администратор",
      isAdmin: true,
      isSuperuser: false,
      isClientPortal: false,
      isTwoFactorEnabled: true,
      isTwoFactorRequired: true,
    },
    login: vi.fn(),
    establishSession: vi.fn(),
    logout: vi.fn(),
  };

  return render(
    <QueryClientProvider client={queryClient}>
      <AuthContext.Provider value={auth}>
        <ThemeProvider>
          <MemoryRouter initialEntries={[initialEntry]}>
            <Routes>
              <Route path="/statistics/work" element={<StatisticsWorkPage />} />
              <Route path="/statistics/finance" element={<StatisticsFinancePage />} />
            </Routes>
          </MemoryRouter>
        </ThemeProvider>
      </AuthContext.Provider>
    </QueryClientProvider>,
  );
}

const context = {
  startDate: "2026-07-01",
  endDate: "2026-07-31",
  timezone: "UTC",
  providerId: "user-1",
  scopeLabel: "Иванова Анна",
  groupBy: "week" as const,
  providers: [{ id: "user-1", displayName: "Иванова Анна" }],
};

const emptyFinanceReport: FinanceReport = {
  context,
  summary: {
    revenue: 0,
    payments: null,
    expenses: null,
    netProfit: null,
    outstandingDebt: null,
    averageRevenuePerVisit: null,
    revenueAppointments: 0,
    organizationOnlyFiguresAvailable: false,
  },
  trend: [],
  expenseCategories: [],
  debtors: [],
  services: [],
};
