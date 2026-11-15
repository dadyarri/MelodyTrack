import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StaffSignInForm } from "./StaffSignInForm";

const session = vi.hoisted(() => ({
  status: "anonymous" as "anonymous" | "authenticating",
  factor: null as {
    method: "authenticator" | "authenticatorOrPasskey";
    canUsePasskey: boolean;
  } | null,
  passkeyReady: false,
  preparingPasskey: false,
  error: null as { message: string; retryAfterSeconds?: number; traceId?: string } | null,
  preparePasskey: vi.fn<() => Promise<void>>(),
  signInWithPasskey: vi.fn<() => Promise<void>>(),
  signInWithPassword: vi.fn<(login: string, password: string) => Promise<void>>(),
  completeFactor: vi.fn<(code: string) => Promise<void>>(),
  completeFactorWithPasskey: vi.fn<() => Promise<void>>(),
  cancelSignIn: vi.fn(),
  clearError: vi.fn(),
}));

vi.mock("@/entities/session", () => ({ useStaffSession: () => session }));
vi.mock("@/shared/lib/passkeys", () => ({ supportsPasskeys: () => true }));

beforeEach(() => {
  vi.stubGlobal(
    "matchMedia",
    vi.fn(() => ({
      matches: false,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  );
  vi.spyOn(document, "visibilityState", "get").mockReturnValue("visible");
  session.status = "anonymous";
  session.factor = null;
  session.passkeyReady = false;
  session.preparingPasskey = false;
  session.error = null;
  for (const method of [
    session.preparePasskey,
    session.signInWithPasskey,
    session.signInWithPassword,
    session.completeFactor,
    session.completeFactorWithPasskey,
    session.cancelSignIn,
    session.clearError,
  ])
    method.mockReset();
  session.preparePasskey.mockResolvedValue(undefined);
  session.signInWithPasskey.mockResolvedValue(undefined);
  session.signInWithPassword.mockResolvedValue(undefined);
  session.completeFactor.mockResolvedValue(undefined);
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("staff sign in", () => {
  it("offers passkey below the password form without passing its username", async () => {
    const view = render(<StaffSignInForm />);
    fireEvent.change(screen.getByLabelText("Логин"), {
      target: { value: "previous-password-login" },
    });

    await waitFor(() => expect(session.preparePasskey).toHaveBeenCalledOnce());

    expect(screen.queryByRole("tablist")).toBeNull();
    expect(screen.getByLabelText("Логин")).toBeTruthy();
    expect(screen.getByLabelText("Пароль", { exact: true })).toBeTruthy();
    const passwordButton = screen.getByRole("button", { name: "Войти" });
    const passkeyButton = screen.getByRole("button", { name: "Войти с ключом" });
    expect(
      passwordButton.compareDocumentPosition(passkeyButton) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(session.signInWithPasskey).not.toHaveBeenCalled();
    session.passkeyReady = true;
    view.rerender(<StaffSignInForm />);
    fireEvent.click(screen.getByRole("button", { name: "Войти с ключом" }));

    expect(session.signInWithPasskey).toHaveBeenCalledWith();
    expect(session.signInWithPassword).not.toHaveBeenCalled();
  });

  it("keeps WebAuthn inactive while its server challenge is being prepared", async () => {
    session.preparingPasskey = true;
    render(<StaffSignInForm />);

    await waitFor(() => expect(session.preparePasskey).toHaveBeenCalledOnce());
    const button = screen.getByRole("button", {
      name: "Войти с ключом",
    }) as HTMLButtonElement;
    fireEvent.click(button);

    expect(button.disabled).toBe(true);
    expect(session.signInWithPasskey).not.toHaveBeenCalled();
  });

  it("allows a failed challenge preparation to be retried explicitly without a request loop", async () => {
    session.error = { message: "Нет соединения с сервером." };
    render(<StaffSignInForm />);
    await waitFor(() => expect(session.preparePasskey).toHaveBeenCalledOnce());

    fireEvent.click(screen.getByRole("button", { name: "Повторить" }));

    expect(session.preparePasskey).toHaveBeenCalledTimes(2);
    expect(session.signInWithPasskey).not.toHaveBeenCalled();
    expect(screen.getByText("Нет соединения с сервером.")).toBeTruthy();
  });

  it("continues to confirm a requested authenticator factor", async () => {
    session.factor = { method: "authenticator", canUsePasskey: false };
    render(<StaffSignInForm />);
    fireEvent.change(screen.getByLabelText("Код подтверждения"), { target: { value: "123456" } });

    fireEvent.click(screen.getByRole("button", { name: "Подтвердить вход" }));

    await waitFor(() => expect(session.completeFactor).toHaveBeenCalledWith("123456"));
    expect(session.preparePasskey).not.toHaveBeenCalled();
    expect(session.signInWithPasskey).not.toHaveBeenCalled();
  });
});
