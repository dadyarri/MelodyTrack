import {
  act,
  cleanup,
  fireEvent,
  render as renderUi,
  screen,
  waitFor,
} from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Notifications } from "@/shared/ui/notifications";
import { StaffInvitationForm } from "./StaffInvitationForm";

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
  completeRegistration: vi.fn<(...args: unknown[]) => Promise<boolean>>(),
  cancelSignIn: vi.fn(),
  clearError: vi.fn(),
}));
const passkeys = vi.hoisted(() => ({ available: true, create: vi.fn() }));
vi.mock("@/entities/session", () => ({ useStaffSession: () => session }));
vi.mock("@/shared/lib/passkeys", () => ({
  supportsPasskeys: () => passkeys.available,
  createPasskeyRegistration: passkeys.create,
}));

const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const initialSetup = {
  challenge: "totp-test",
  authenticatorSecret: "JBSWY3DPEHPK3PXP",
  optionsJson: "{}",
};
const nextSetup = { challenge: "passkey-test", optionsJson: "{}" };

beforeEach(() => {
  vi.stubGlobal("navigator", { credentials: { create: vi.fn() } });
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
  session.completeRegistration.mockReset();
  session.cancelSignIn.mockReset();
  session.clearError.mockReset();
  passkeys.available = true;
  passkeys.create.mockReset();
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

async function enterPassword() {
  await screen.findByLabelText("Логин");
  fireEvent.change(screen.getByLabelText("Логин"), { target: { value: "teacher.demo" } });
  fireEvent.change(screen.getByLabelText("Пароль", { exact: true }), {
    target: { value: "test-only" },
  });
  fireEvent.change(screen.getByLabelText("Повторите пароль"), { target: { value: "test-only" } });
  fireEvent.click(screen.getByRole("button", { name: "Продолжить" }));
  await screen.findByLabelText("Код из приложения");
}

async function advanceTeacherToPasskey(fetch: ReturnType<typeof vi.fn>, onComplete = vi.fn()) {
  fetch
    .mockResolvedValueOnce(jsonResponse({ role: "teacher" }))
    .mockResolvedValueOnce(jsonResponse(initialSetup))
    .mockResolvedValueOnce(jsonResponse(nextSetup));
  vi.stubGlobal("fetch", fetch);
  render(<StaffInvitationForm invitation="synthetic-invitation" onComplete={onComplete} />);
  await enterPassword();
  fireEvent.click(screen.getByRole("button", { name: "Пропустить аутентификатор" }));
  await screen.findByRole("button", { name: "Создать ключ и войти" });
  return onComplete;
}

describe("invitation acceptance", () => {
  it("confirms the authenticator before reaching the passkey step and completing registration", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ role: "superuser" }))
      .mockResolvedValueOnce(jsonResponse(initialSetup))
      .mockResolvedValueOnce(jsonResponse(nextSetup));
    vi.stubGlobal("fetch", fetch);
    session.completeRegistration.mockResolvedValue(true);
    const onComplete = vi.fn();
    render(<StaffInvitationForm invitation="synthetic-invitation" onComplete={onComplete} />);
    await enterPassword();

    expect(session.completeRegistration).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Создать ключ и войти" })).toBeNull();
    const options = fetch.mock.calls[1]?.[1] as RequestInit;
    expect(await new Response(options.body).json()).toEqual({
      link: "synthetic-invitation",
      login: "teacher.demo",
      password: "test-only",
      authenticator: true,
      secondFactor: false,
    });
    fireEvent.change(screen.getByLabelText("Код из приложения"), { target: { value: "123456" } });
    fireEvent.click(screen.getByRole("button", { name: "Подтвердить код" }));
    await screen.findByRole("button", { name: "Пропустить ключ доступа" });

    const confirmation = fetch.mock.calls[2]?.[1] as RequestInit;
    expect(await new Response(confirmation.body).json()).toEqual({
      challenge: "totp-test",
      code: "123456",
      skip: false,
    });
    expect(screen.queryByLabelText("Ключ аутентификатора")).toBeNull();
    expect(screen.queryByLabelText("Код из приложения")).toBeNull();
    expect(session.completeRegistration).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Пропустить ключ доступа" }));
    await waitFor(() => expect(onComplete).toHaveBeenCalledOnce());
    expect(session.completeRegistration).toHaveBeenCalledWith("passkey-test", undefined, undefined);
  });

  it("skips password validation but keeps the authenticator mandatory for a passwordless superuser", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ role: "superuser" }))
      .mockResolvedValueOnce(jsonResponse(initialSetup));
    vi.stubGlobal("fetch", fetch);
    render(<StaffInvitationForm invitation="synthetic-invitation" onComplete={vi.fn()} />);
    await screen.findByLabelText("Логин");
    fireEvent.change(screen.getByLabelText("Логин"), { target: { value: "teacher.demo" } });

    fireEvent.click(screen.getByRole("button", { name: "Пропустить пароль" }));
    await screen.findByLabelText("Код из приложения");

    const options = fetch.mock.calls[1]?.[1] as RequestInit;
    expect(await new Response(options.body).json()).toEqual({
      link: "synthetic-invitation",
      login: "teacher.demo",
      authenticator: true,
      secondFactor: false,
    });
    expect(screen.queryByRole("button", { name: "Пропустить аутентификатор" })).toBeNull();
    expect(session.completeRegistration).not.toHaveBeenCalled();
  });

  it("requires a passkey when a superuser with a password skips the authenticator", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ role: "superuser" }))
      .mockResolvedValueOnce(jsonResponse(initialSetup))
      .mockResolvedValueOnce(jsonResponse(nextSetup));
    vi.stubGlobal("fetch", fetch);
    render(<StaffInvitationForm invitation="synthetic-invitation" onComplete={vi.fn()} />);
    await enterPassword();

    fireEvent.click(screen.getByRole("button", { name: "Пропустить аутентификатор" }));
    await screen.findByRole("button", { name: "Создать ключ и войти" });

    expect(screen.queryByRole("button", { name: "Пропустить ключ доступа" })).toBeNull();
    const confirmation = fetch.mock.calls[2]?.[1] as RequestInit;
    expect(await new Response(confirmation.body).json()).toEqual({
      challenge: "totp-test",
      skip: true,
    });
    expect(session.completeRegistration).not.toHaveBeenCalled();
  });

  it("shows an expired invitation without opening the registration form", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({}, 401)));
    render(<StaffInvitationForm invitation="expired-invitation" onComplete={vi.fn()} />);

    await screen.findByText("Приглашение недействительно или уже использовано.");

    expect(screen.queryByLabelText("Логин")).toBeNull();
    expect(session.completeRegistration).not.toHaveBeenCalled();
  });

  it("keeps password and authenticator available when the browser cannot create passkeys", async () => {
    passkeys.available = false;
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse({ role: "superuser" }))
        .mockResolvedValueOnce(jsonResponse(initialSetup)),
    );
    render(<StaffInvitationForm invitation="synthetic-invitation" onComplete={vi.fn()} />);
    await screen.findByLabelText("Логин");

    expect(
      (screen.getByRole("button", { name: "Пропустить пароль" }) as HTMLButtonElement).disabled,
    ).toBe(true);
    await enterPassword();

    expect(
      (
        screen.getByRole("button", {
          name: "Пропустить аутентификатор",
        }) as HTMLButtonElement
      ).disabled,
    ).toBe(true);
    expect(
      (screen.getByRole("button", { name: "Подтвердить код" }) as HTMLButtonElement).disabled,
    ).toBe(false);
  });

  it("lets a teacher with a password skip both optional setup steps", async () => {
    passkeys.available = false;
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ role: "teacher" }))
      .mockResolvedValueOnce(jsonResponse(initialSetup))
      .mockResolvedValueOnce(jsonResponse(nextSetup));
    vi.stubGlobal("fetch", fetch);
    session.completeRegistration.mockResolvedValue(true);
    const onComplete = vi.fn();
    render(<StaffInvitationForm invitation="synthetic-invitation" onComplete={onComplete} />);
    await enterPassword();

    fireEvent.click(screen.getByRole("button", { name: "Пропустить аутентификатор" }));
    await screen.findByRole("button", { name: "Пропустить ключ доступа" });
    fireEvent.click(screen.getByRole("button", { name: "Пропустить ключ доступа" }));

    await waitFor(() => expect(onComplete).toHaveBeenCalledOnce());
    expect(session.completeRegistration).toHaveBeenCalledWith("passkey-test", undefined, undefined);
    expect(passkeys.create).not.toHaveBeenCalled();
  });

  it("discards the one-use setup after a rejected authenticator code", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ role: "superuser" }))
      .mockResolvedValueOnce(jsonResponse(initialSetup))
      .mockResolvedValueOnce(jsonResponse({}, 401));
    vi.stubGlobal("fetch", fetch);
    render(<StaffInvitationForm invitation="synthetic-invitation" onComplete={vi.fn()} />);
    await enterPassword();
    fireEvent.change(screen.getByLabelText("Код из приложения"), { target: { value: "000000" } });

    fireEvent.click(screen.getByRole("button", { name: "Подтвердить код" }));
    await screen.findByText("Код не подошёл или настройка устарела. Начните настройку заново.");

    expect(screen.queryByLabelText("Ключ аутентификатора")).toBeNull();
    expect(screen.queryByLabelText("Код из приложения")).toBeNull();
    expect((screen.getByLabelText("Пароль", { exact: true }) as HTMLInputElement).value).toBe("");
    expect(fetch).toHaveBeenCalledTimes(3);
    expect(session.completeRegistration).not.toHaveBeenCalled();
  });

  it("ignores a late authenticator response after restarting setup", async () => {
    let finishConfirmation: ((value: Response) => void) | undefined;
    let confirmationSignal: AbortSignal | undefined;
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ role: "superuser" }))
      .mockResolvedValueOnce(jsonResponse(initialSetup))
      .mockImplementationOnce((_url: string, init: RequestInit) => {
        confirmationSignal = init.signal as AbortSignal;
        return new Promise<Response>((resolve) => {
          finishConfirmation = resolve;
        });
      });
    vi.stubGlobal("fetch", fetch);
    render(<StaffInvitationForm invitation="synthetic-invitation" onComplete={vi.fn()} />);
    await enterPassword();
    fireEvent.change(screen.getByLabelText("Код из приложения"), {
      target: { value: "123456" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Подтвердить код" }));
    await waitFor(() => expect(confirmationSignal).toBeDefined());

    fireEvent.click(screen.getByRole("button", { name: "Начать заново" }));
    expect(confirmationSignal?.aborted).toBe(true);
    expect(screen.getByLabelText("Логин")).toBeTruthy();

    await act(async () => {
      finishConfirmation?.(jsonResponse(nextSetup));
      await Promise.resolve();
    });
    expect(screen.queryByRole("button", { name: "Создать ключ и войти" })).toBeNull();
    expect(session.completeRegistration).not.toHaveBeenCalled();
  });

  it("ignores a passkey chooser result after restarting registration", async () => {
    let finishChooser: ((value: Record<string, unknown>) => void) | undefined;
    let chooserSignal: AbortSignal | undefined;
    passkeys.create.mockImplementation((_options: string, signal: AbortSignal) => {
      chooserSignal = signal;
      return new Promise<Record<string, unknown>>((resolve) => {
        finishChooser = resolve;
      });
    });
    const onComplete = await advanceTeacherToPasskey(vi.fn());
    fireEvent.click(screen.getByRole("button", { name: "Создать ключ и войти" }));
    await waitFor(() => expect(chooserSignal).toBeDefined());

    fireEvent.click(screen.getByRole("button", { name: "Начать заново" }));
    expect(chooserSignal?.aborted).toBe(true);
    await act(async () => {
      finishChooser?.({ id: "late-passkey" });
      await Promise.resolve();
    });

    expect(session.completeRegistration).not.toHaveBeenCalled();
    expect(onComplete).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Логин")).toBeTruthy();
  });

  it("cancels a pending completion and ignores its late success after restart", async () => {
    let finishRegistration: ((accepted: boolean) => void) | undefined;
    session.completeRegistration.mockImplementation(
      () =>
        new Promise<boolean>((resolve) => {
          finishRegistration = resolve;
        }),
    );
    const onComplete = await advanceTeacherToPasskey(vi.fn());
    fireEvent.click(screen.getByRole("button", { name: "Пропустить ключ доступа" }));
    await waitFor(() => expect(finishRegistration).toBeDefined());

    fireEvent.click(screen.getByRole("button", { name: "Начать заново" }));
    expect(session.cancelSignIn).toHaveBeenCalledOnce();
    await act(async () => {
      finishRegistration?.(true);
      await Promise.resolve();
    });

    expect(session.completeRegistration).toHaveBeenCalledWith("passkey-test", undefined, undefined);
    expect(onComplete).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Логин")).toBeTruthy();
  });
});
