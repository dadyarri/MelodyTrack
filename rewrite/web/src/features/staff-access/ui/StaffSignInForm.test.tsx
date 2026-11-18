import { cleanup, fireEvent, render as renderUi, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Notifications } from "@/shared/ui/notifications";
import { StaffSignInForm } from "./StaffSignInForm";

function render(ui: ReactNode) {
  return renderUi(ui, {
    wrapper: ({ children }) => (
      <>
        {children}
        <Notifications appearance="light" />
      </>
    ),
  });
}

const session = vi.hoisted(() => ({
  status: "anonymous" as "anonymous" | "authenticating" | "authenticated",
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
  it("celebrates a completed sign-in and prevents another attempt during the final gesture", () => {
    session.status = "authenticated";
    const view = render(<StaffSignInForm />);

    expect(view.container.querySelector("[data-pose]")?.getAttribute("data-pose")).toBe("success");
    expect(screen.getByRole("status").textContent).toBe("Вход выполнен");
    expect((screen.getByRole("button", { name: "Войти" }) as HTMLButtonElement).disabled).toBe(
      true,
    );
    expect(screen.queryByRole("button", { name: "Отменить попытку" })).toBeNull();
  });

  it("raises a key for passkey entry and keeps it raised while authentication is pending", () => {
    session.passkeyReady = true;
    const view = render(<StaffSignInForm />);
    const button = screen.getByRole("button", { name: "Войти с ключом" });
    const mascot = view.container.querySelector("[data-pose]") as HTMLElement;

    fireEvent.focus(button);
    fireEvent.click(button);

    expect(mascot.dataset.pose).toBe("passkey");
    expect(session.signInWithPasskey).toHaveBeenCalledOnce();

    session.status = "authenticating";
    view.rerender(<StaffSignInForm />);
    fireEvent.blur(button);

    expect(mascot.dataset.pose).toBe("passkey");

    session.status = "anonymous";
    view.rerender(<StaffSignInForm />);

    expect(mascot.dataset.pose).toBe("idle");
  });

  it("follows the login caret and rests when the field loses focus", () => {
    const view = render(<StaffSignInForm />);
    const mascot = view.container.querySelector("[data-pose]") as HTMLElement;
    const login = screen.getByLabelText("Логин") as HTMLInputElement;

    fireEvent.focus(login);
    fireEvent.input(login, { target: { value: "music-teacher", selectionStart: 12 } });
    fireEvent.select(login, { target: { selectionStart: 12 } });

    expect(mascot.dataset.pose).toBe("login");
    const rightwardLook = mascot.style.getPropertyValue("--guitar-look-x");

    fireEvent.select(login, { target: { selectionStart: 0 } });

    expect(mascot.style.getPropertyValue("--guitar-look-x")).not.toBe(rightwardLook);

    fireEvent.blur(login);

    expect(mascot.dataset.pose).toBe("idle");
    expect(mascot.getAttribute("aria-hidden")).toBe("true");
  });

  it("covers its eyes for a password and peeks only while its visibility is enabled", () => {
    const view = render(<StaffSignInForm />);
    const mascot = view.container.querySelector("[data-pose]") as HTMLElement;
    const password = screen.getByLabelText("Пароль", { exact: true }) as HTMLInputElement;

    fireEvent.focus(password);

    expect(mascot.dataset.pose).toBe("cover");
    expect(password.type).toBe("password");

    const show = screen.getByRole("button", { name: "Показать пароль" });
    fireEvent.blur(password, { relatedTarget: show });
    fireEvent.focus(show, { relatedTarget: password });
    fireEvent.click(show);

    expect(mascot.dataset.pose).toBe("peek");
    expect(password.type).toBe("text");

    fireEvent.click(screen.getByRole("button", { name: "Скрыть пароль" }));

    expect(mascot.dataset.pose).toBe("cover");
    expect(password.type).toBe("password");

    fireEvent.blur(show, { relatedTarget: screen.getByRole("button", { name: "Войти" }) });

    expect(mascot.dataset.pose).toBe("idle");
  });

  it("covers its eyes again when password entry is restarted after a second factor", () => {
    const view = render(<StaffSignInForm />);
    fireEvent.focus(screen.getByLabelText("Пароль", { exact: true }));
    fireEvent.click(screen.getByRole("button", { name: "Показать пароль" }));

    session.factor = { method: "authenticator", canUsePasskey: false };
    view.rerender(<StaffSignInForm />);
    session.factor = null;
    view.rerender(<StaffSignInForm />);
    const password = screen.getByLabelText("Пароль", { exact: true }) as HTMLInputElement;
    fireEvent.focus(password);

    expect(password.type).toBe("password");
    expect(view.container.querySelector("[data-pose]")?.getAttribute("data-pose")).toBe("cover");
  });

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
    await screen.findByText("Не удалось выполнить вход");
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

  it("offers a passkey only for a factor that allows it and clears the code first", async () => {
    session.factor = { method: "authenticatorOrPasskey", canUsePasskey: true };
    render(<StaffSignInForm />);
    fireEvent.change(screen.getByLabelText("Код подтверждения"), { target: { value: "123456" } });

    fireEvent.click(screen.getByRole("button", { name: "Использовать ключ доступа" }));

    expect(session.completeFactorWithPasskey).toHaveBeenCalledOnce();
    expect(session.completeFactor).not.toHaveBeenCalled();
    expect((screen.getByLabelText("Код подтверждения") as HTMLInputElement).value).toBe("");
    expect(session.signInWithPassword).not.toHaveBeenCalled();
  });
});
