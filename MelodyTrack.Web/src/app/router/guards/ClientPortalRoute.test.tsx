import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";

import { hasClientPortalAccess, useAuth } from "@/entities/session";

import { ClientPortalRoute } from "./ClientPortalRoute";

vi.mock("@/entities/session", () => ({ useAuth: vi.fn(), hasClientPortalAccess: vi.fn() }));

afterEach(() => {
  vi.resetAllMocks();
});

function renderPortal() {
  return render(
    <MemoryRouter initialEntries={["/portal/courses"]}>
      <Routes>
        <Route
          path="/portal/courses"
          element={
            <ClientPortalRoute>
              <p>Курсы ученика</p>
            </ClientPortalRoute>
          }
        />
        <Route path="/portal/error" element={<p>Нужен вход</p>} />
        <Route path="/portal/alias" element={<p>Выберите псевдоним</p>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("Client portal route", () => {
  it("redirects an unauthorized session before showing portal content", () => {
    vi.mocked(useAuth).mockReturnValue({ isLoading: false, user: null } as ReturnType<typeof useAuth>);
    vi.mocked(hasClientPortalAccess).mockReturnValue(false);

    renderPortal();

    expect(screen.getByText("Нужен вход")).toBeInTheDocument();
    expect(screen.queryByText("Курсы ученика")).not.toBeInTheDocument();
  });

  it("requires an alias before showing a directly opened portal section", () => {
    vi.mocked(useAuth).mockReturnValue({ isLoading: false, user: { portalAlias: null } } as ReturnType<typeof useAuth>);
    vi.mocked(hasClientPortalAccess).mockReturnValue(true);

    renderPortal();

    expect(screen.getByText("Выберите псевдоним")).toBeInTheDocument();
    expect(screen.queryByText("Курсы ученика")).not.toBeInTheDocument();
  });
});
