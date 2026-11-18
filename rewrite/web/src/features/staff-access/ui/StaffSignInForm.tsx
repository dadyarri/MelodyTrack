import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { useStaffSession } from "@/entities/session";
import { supportsPasskeys } from "@/shared/lib/passkeys";
import { useErrorNotification } from "@/shared/ui/notifications";
import { PaperButton, PaperInput, PaperPasswordInput } from "@/shared/ui/paper";
import styles from "./StaffSignInForm.module.css";

type PrimaryValues = { login: string; password: string };
type FactorValues = { code: string };

export function StaffSignInForm() {
  const session = useStaffSession();
  const [method, setMethod] = useState<"password" | "passkey">("password");
  const primaryForm = useForm<PrimaryValues>({ defaultValues: { login: "", password: "" } });
  const factorForm = useForm<FactorValues>({ defaultValues: { code: "" } });
  const busy = session.status === "authenticating";
  const passkeysSupported = supportsPasskeys();
  const { preparePasskey, factor } = session;
  const sessionError = session.error
    ? [
        session.error.message,
        session.error.retryAfterSeconds
          ? `Повторите через ${session.error.retryAfterSeconds} сек.`
          : null,
        session.error.traceId ? `Код обращения: ${session.error.traceId}` : null,
      ]
        .filter(Boolean)
        .join(" · ")
    : null;
  useErrorNotification(sessionError, { title: "Не удалось выполнить вход" });

  useEffect(() => {
    if (!passkeysSupported || factor || busy) return;
    const prepare = () => {
      if (document.visibilityState === "visible") void preparePasskey();
    };
    prepare();
    window.addEventListener("focus", prepare);
    document.addEventListener("visibilitychange", prepare);
    return () => {
      window.removeEventListener("focus", prepare);
      document.removeEventListener("visibilitychange", prepare);
    };
  }, [passkeysSupported, factor, busy, preparePasskey]);

  const submitPrimary = (values: PrimaryValues) => {
    setMethod("password");
    const login = values.login.trim();
    const password = values.password;
    primaryForm.resetField("password");
    void session.signInWithPassword(login, password);
  };

  const submitFactor = (values: FactorValues) => {
    setMethod("password");
    const code = values.code;
    factorForm.reset();
    void session.completeFactor(code);
  };

  return (
    <div className={styles.form}>
      {session.factor ? (
        <section aria-labelledby="staff-access-factor-title" className={styles.fields}>
          <h2 id="staff-access-factor-title">Подтвердите вход</h2>
          <p className={styles.description}>Введите код из приложения-аутентификатора.</p>
          <form
            className={styles.fields}
            onSubmit={factorForm.handleSubmit(submitFactor)}
            noValidate
          >
            <div className={styles.field}>
              <label htmlFor="staff-factor-code">Код подтверждения</label>
              <PaperInput
                id="staff-factor-code"
                autoComplete="one-time-code"
                inputMode="numeric"
                maxLength={6}
                autoFocus
                placeholder="000000"
                disabled={busy}
                aria-invalid={Boolean(factorForm.formState.errors.code) || undefined}
                aria-describedby={
                  factorForm.formState.errors.code ? "staff-factor-code-error" : undefined
                }
                {...factorForm.register("code", {
                  required: "Введите код из приложения-аутентификатора.",
                  pattern: { value: /^\d{6}$/, message: "Код состоит из шести цифр." },
                })}
              />
              {factorForm.formState.errors.code && (
                <span id="staff-factor-code-error" className={styles.fieldError} role="alert">
                  {factorForm.formState.errors.code.message}
                </span>
              )}
            </div>
            <PaperButton type="submit" variant="primary" loading={busy}>
              Подтвердить вход
            </PaperButton>
          </form>
          {session.factor.canUsePasskey && (
            <PaperButton
              className={styles.secondary}
              disabled={busy}
              onClick={() => {
                setMethod("passkey");
                factorForm.reset();
                void session.completeFactorWithPasskey();
              }}
            >
              Использовать ключ доступа
            </PaperButton>
          )}
          <PaperButton
            className={styles.textButton}
            onClick={() => {
              factorForm.reset();
              session.cancelSignIn();
            }}
          >
            Начать вход заново
          </PaperButton>
        </section>
      ) : (
        <>
          <form
            className={styles.fields}
            onSubmit={primaryForm.handleSubmit(submitPrimary)}
            noValidate
          >
            <div className={styles.field}>
              <label htmlFor="staff-login">Логин</label>
              <PaperInput
                id="staff-login"
                autoComplete="username"
                autoCapitalize="none"
                autoCorrect="off"
                spellCheck={false}
                disabled={busy}
                aria-invalid={Boolean(primaryForm.formState.errors.login) || undefined}
                aria-describedby={
                  primaryForm.formState.errors.login ? "staff-login-error" : undefined
                }
                {...primaryForm.register("login", {
                  required: "Введите ваш логин.",
                  validate: (value) => value.trim().length > 0 || "Введите ваш логин.",
                })}
              />
              {primaryForm.formState.errors.login && (
                <span id="staff-login-error" className={styles.fieldError} role="alert">
                  {primaryForm.formState.errors.login.message}
                </span>
              )}
            </div>
            <div className={styles.field}>
              <label htmlFor="staff-password">Пароль</label>
              <PaperPasswordInput
                id="staff-password"
                autoComplete="current-password"
                disabled={busy}
                aria-invalid={Boolean(primaryForm.formState.errors.password) || undefined}
                aria-describedby={
                  primaryForm.formState.errors.password ? "staff-password-error" : undefined
                }
                {...primaryForm.register("password", { required: "Введите пароль." })}
              />
              {primaryForm.formState.errors.password && (
                <span id="staff-password-error" className={styles.fieldError} role="alert">
                  {primaryForm.formState.errors.password.message}
                </span>
              )}
            </div>
            <PaperButton type="submit" variant="primary" loading={busy && method === "password"}>
              Войти
            </PaperButton>
          </form>
          <section className={styles.passkey} aria-label="Вход с ключом доступа">
            {!passkeysSupported && (
              <p className={styles.info} role="status">
                Ключи доступа здесь недоступны. Откройте приложение по HTTPS или используйте вход по
                паролю.
              </p>
            )}
            <PaperButton
              loading={session.preparingPasskey || (busy && method === "passkey")}
              aria-label="Войти с ключом"
              disabled={busy || !session.passkeyReady || !passkeysSupported}
              onClick={() => {
                setMethod("passkey");
                primaryForm.resetField("password");
                void session.signInWithPasskey();
              }}
            >
              Войти с ключом
            </PaperButton>
            {passkeysSupported &&
              !session.passkeyReady &&
              !session.preparingPasskey &&
              session.error && (
                <PaperButton className={styles.secondary} onClick={() => void preparePasskey()}>
                  Повторить
                </PaperButton>
              )}
          </section>
          {busy && (
            <PaperButton
              className={styles.textButton}
              onClick={() => {
                primaryForm.resetField("password");
                session.cancelSignIn();
              }}
            >
              Отменить попытку
            </PaperButton>
          )}
        </>
      )}
    </div>
  );
}
