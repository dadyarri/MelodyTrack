import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import type * as VacationModule from "@/entities/vacation";
import type { Vacation, vacationsApi } from "@/entities/vacation";

import { VacationManagement } from "./VacationManagement";

const api = vi.hoisted(() => ({
  list: vi.fn<typeof vacationsApi.list>(),
  create: vi.fn<typeof vacationsApi.create>(),
  update: vi.fn<typeof vacationsApi.update>(),
  remove: vi.fn<typeof vacationsApi.remove>(),
  end: vi.fn<typeof vacationsApi.end>(),
  candidates: vi.fn<typeof vacationsApi.candidates>(),
  restore: vi.fn<typeof vacationsApi.restore>(),
}));
vi.mock("@/entities/session", () => ({ useAuth: () => ({ user: { id: "teacher", isSuperuser: false } }) }));
vi.mock("@/entities/user", () => ({
  userQueryKeys: { availability: (id?: string) => ["users", "availability", id] },
  usersApi: { getAvailability: () => Promise.resolve({ timezone: "Europe/Moscow" }) },
}));
vi.mock("@/entities/vacation", async (importOriginal) => ({
  ...(await importOriginal<typeof VacationModule>()),
  vacationsApi: api,
}));

const vacation: Vacation = {
  id: "vacation",
  subjectType: "staff",
  subjectId: "teacher",
  version: 2,
  state: "active",
  startDate: "2030-06-03T09:00:00Z",
  endDate: "2030-06-07T18:00:00Z",
  timezone: "Europe/Moscow",
  requiresApproval: true,
  canManage: true,
  canEnd: true,
};
let queryClient: QueryClient;
beforeEach(() => {
  vi.resetAllMocks();
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  api.list.mockResolvedValue([vacation]);
  api.candidates.mockResolvedValue([]);
});
afterEach(() => {
  queryClient.clear();
});
async function showManagement() {
  return render(
    <QueryClientProvider client={queryClient}>
      <VacationManagement allowCreate={false} />
    </QueryClientProvider>,
  );
}

describe("vacation management", () => {
  it("sends a deletion for approval with the displayed version", async () => {
    api.remove.mockResolvedValue({ requiresApproval: true });
    const screen = await showManagement();
    await screen.getByRole("button", { name: "Удалить", exact: true }).click();
    await page.getByRole("button", { name: "Отправить заявку", exact: true }).click();

    await expect.poll(() => api.remove.mock.calls).toEqual([["vacation", 2, undefined]]);
    await expect.element(screen.getByText(/Заявка отправлена/)).toBeVisible();
    expect(api.restore).not.toHaveBeenCalled();
  });
  it("ends immediately and restores only the checked available lesson", async () => {
    api.end.mockResolvedValue({ requiresApproval: false, vacation: { ...vacation, state: "ended", version: 3, canEnd: false } });
    api.candidates.mockResolvedValue([
      {
        id: "selected",
        startDate: "2030-06-04T12:00:00Z",
        endDate: "2030-06-04T13:00:00Z",
        serviceName: "Вокал",
        available: true,
        blockers: [],
      },
      {
        id: "other",
        startDate: "2030-06-05T12:00:00Z",
        endDate: "2030-06-05T13:00:00Z",
        serviceName: "Гитара",
        available: true,
        blockers: [],
      },
      {
        id: "blocked",
        startDate: "2030-06-06T12:00:00Z",
        endDate: "2030-06-06T13:00:00Z",
        serviceName: "Фортепиано",
        available: false,
        blockers: ["Клиент в отпуске"],
      },
    ]);
    api.restore.mockResolvedValue(undefined);
    const screen = await showManagement();
    await screen.getByRole("button", { name: "Завершить сейчас", exact: true }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Завершить сейчас", exact: true }).click();

    await expect.poll(() => api.end.mock.calls).toEqual([["vacation", 2]]);
    await expect.element(page.getByRole("checkbox", { name: /Фортепиано/ })).toBeDisabled();
    await expect.element(page.getByRole("button", { name: "Восстановить выбранные" })).toBeDisabled();
    expect(api.restore).not.toHaveBeenCalled();
    await page.getByRole("checkbox", { name: /Вокал/ }).click();
    await page.getByRole("button", { name: "Восстановить выбранные" }).click();

    await expect.poll(() => api.restore.mock.calls).toEqual([["vacation", { expectedVersion: 3, interruptionIds: ["selected"] }]]);
    await expect.element(screen.getByText("Выбранные занятия восстановлены.")).toBeVisible();
  });
});
