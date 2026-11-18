import { fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import { AppShell } from "./AppShell";

vi.mock("@/entities/session", () => ({
  useStaffSession: () => ({
    staff: {
      staffId: "staff-1",
      login: "anna",
      role: "employee",
      permissions: [],
    },
    signOut: vi.fn(),
    error: null,
  }),
}));
vi.mock("@/features/employee-training", () => ({
  useEmployeeTraining: () => ({ ready: true, active: false }),
}));
vi.mock("@/features/view-release-notes", () => ({
  ReleaseNotesProvider: ({ children }: { children: ReactNode }) => children,
  ReleaseVersion: () => <span>Версия приложения</span>,
}));
vi.mock("@/shared/ui/notifications", () => ({
  useErrorNotification: () => undefined,
}));

describe("app shell account menu", () => {
  it("does not expose a local storage settings action", () => {
    render(
      <MemoryRouter>
        <AppShell appearance="light" onToggleAppearance={vi.fn()} />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Меню пользователя anna" }));

    expect(screen.queryByRole("menuitem", { name: "Локальные данные" })).toBeNull();
  });
});
