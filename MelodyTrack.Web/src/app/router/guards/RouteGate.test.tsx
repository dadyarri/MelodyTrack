import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { AuthContextValue, MeResponse } from "@/entities/session";

import { RouteGate } from "./RouteGate";

let authState: AuthContextValue;

vi.mock("@/entities/session", () => ({
  useAuth: () => authState,
}));

const adminUser: MeResponse = {
  id: "user-1",
  email: "admin@example.com",
  firstName: "Admin",
  lastName: "User",
  roleDisplayName: "Administrator",
  isAdmin: true,
  isSuperuser: false,
  isClientPortal: false,
  isTwoFactorEnabled: true,
  isTwoFactorRequired: true,
};

function createAuthState(user: MeResponse | null): AuthContextValue {
  return {
    isLoading: false,
    isAuthenticated: Boolean(user),
    user,
    login: vi.fn(),
    establishSession: vi.fn(),
    logout: vi.fn(),
  };
}

function renderAdminRoute() {
  render(
    <MemoryRouter initialEntries={["/private"]}>
      <Routes>
        <Route
          path="/private"
          element={
            <RouteGate allow={(user) => user?.isAdmin === true} redirectTo="/login">
              <h1>Private content</h1>
            </RouteGate>
          }
        />
        <Route path="/login" element={<h1>Login</h1>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("RouteGate", () => {
  beforeEach(() => {
    authState = createAuthState(null);
  });

  it("renders protected content for an allowed user", () => {
    authState = createAuthState(adminUser);

    renderAdminRoute();

    expect(screen.getByRole("heading", { name: "Private content" })).toBeInTheDocument();
  });

  it("redirects a denied user", () => {
    renderAdminRoute();

    expect(screen.getByRole("heading", { name: "Login" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Private content" })).not.toBeInTheDocument();
  });
});
