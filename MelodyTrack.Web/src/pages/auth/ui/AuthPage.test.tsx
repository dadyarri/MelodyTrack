import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { App as AntdApp } from "antd";
import { MemoryRouter } from "react-router";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

import { AppError } from "@/shared/api";

import { AuthPage } from "./AuthPage";

const { loginMock } = vi.hoisted(() => ({
  loginMock: vi.fn(),
}));

vi.mock("@/entities/session", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/entities/session")>();

  return {
    ...actual,
    authApi: {
      ...actual.authApi,
      login: loginMock,
    },
    useAuth: () => ({
      establishSession: vi.fn(),
      isAuthenticated: false,
      isLoading: false,
      login: vi.fn(),
      logout: vi.fn(),
      user: null,
    }),
  };
});

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    configurable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
});

beforeEach(() => {
  loginMock.mockReset();
});

describe("AuthPage password reset guidance", () => {
  it("shows migration guidance after an unauthorized initial login", async () => {
    loginMock.mockRejectedValueOnce(new AppError("Неверная почта или пароль", { kind: "authentication", status: 401 }));
    renderAuthPage();

    submitLogin();

    await waitFor(() => {
      expect(loginMock).toHaveBeenCalledWith({ email: "user@example.com", password: "wrong-password" });
    });
    expect(await screen.findByText("Не удается войти после обновления безопасности?")).toBeInTheDocument();
    expect(screen.getByText(/его необходимо сбросить/)).toBeInTheDocument();
  });

  it("keeps migration guidance hidden for a connection failure", async () => {
    loginMock.mockRejectedValueOnce(new AppError("Сервер недоступен", { kind: "network" }));
    renderAuthPage();

    submitLogin();

    await waitFor(() => {
      expect(loginMock).toHaveBeenCalledOnce();
    });
    expect(screen.queryByText("Не удается войти после обновления безопасности?")).not.toBeInTheDocument();
  });
});

function renderAuthPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      mutations: { retry: false },
      queries: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <AntdApp>
        <MemoryRouter>
          <AuthPage />
        </MemoryRouter>
      </AntdApp>
    </QueryClientProvider>,
  );
}

function submitLogin() {
  fireEvent.change(screen.getByLabelText("Email"), { target: { value: "user@example.com" } });
  fireEvent.change(screen.getByLabelText("Пароль"), { target: { value: "wrong-password" } });
  fireEvent.click(screen.getByRole("button", { name: "Продолжить" }));
}
