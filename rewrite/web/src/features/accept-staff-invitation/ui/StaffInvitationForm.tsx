import { QRCodeSVG } from "qrcode.react";
import { useCallback, useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { useStaffSession } from "@/entities/session";
import {
  apiOptions,
  invalidResponse,
  isAbortError,
  type StaffIdentity,
  staffApi,
  toApiError,
} from "@/shared/api";
import { createPasskeyRegistration, supportsPasskeys } from "@/shared/lib/passkeys";
import { useErrorNotification } from "@/shared/ui/notifications";
import { PaperButton, PaperInput, PaperPasswordInput } from "@/shared/ui/paper";
import {
  authenticatorUri,
  canSkipAuthenticator,
  canSkipPasskey,
  readAuthenticatorSetup,
  readPasskeySetup,
} from "../model/registration";
import styles from "./StaffInvitationForm.module.css";

type Preview =
  | { kind: "loading" }
  | { kind: "invalid" }
  | { kind: "failed" }
  | { kind: "ready"; role: StaffIdentity["role"] };
type Setup = (
  | ({ step: "authenticator" } & ReturnType<typeof readAuthenticatorSetup>)
  | ({ step: "passkey"; authenticatorConfirmed: boolean } & ReturnType<typeof readPasskeySetup>)
) & { login: string; hasPassword: boolean };
type Credentials = { login: string; password: string; confirmation: string };
type FactorValues = { code: string };

const steps = ["Пароль", "Аутентификатор", "Ключ доступа"];

export function StaffInvitationForm({
  invitation,
  onComplete,
}: {
  invitation: string;
  onComplete: () => void;
}) {
  const session = useStaffSession();
  const form = useForm<Credentials>({
    defaultValues: { login: "", password: "", confirmation: "" },
  });
  const codeForm = useForm<FactorValues>({ defaultValues: { code: "" } });
  const [preview, setPreview] = useState<Preview>({ kind: "loading" });
  const [setup, setSetup] = useState<Setup | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);
  const previewRequest = useRef<AbortController | null>(null);
  const generation = useRef(0);
  const providerPending = useRef(false);
  const completed = useRef(false);
  const passkeysAvailable =
    supportsPasskeys() && typeof navigator.credentials.create === "function";
  const { cancelSignIn, clearError } = session;

  const loadPreview = useCallback(() => {
    previewRequest.current?.abort();
    const controller = new AbortController();
    previewRequest.current = controller;
    setPreview({ kind: "loading" });
    setError(null);
    void staffApi()
      .staff.register.invitation.post({ link: invitation }, apiOptions(controller.signal))
      .then((response) => {
        if (controller.signal.aborted) return;
        const role = response?.role;
        if (role !== "superuser" && role !== "administrator" && role !== "teacher")
          throw invalidResponse();
        setPreview({ kind: "ready", role });
      })
      .catch((issue: unknown) => {
        if (controller.signal.aborted || isAbortError(issue)) return;
        const apiError = toApiError(issue);
        if (apiError.status === 401) {
          setPreview({ kind: "invalid" });
          setError(null);
        } else {
          setPreview({ kind: "failed" });
          setError(apiError.message);
        }
      });
  }, [invitation]);

  useErrorNotification(preview.kind === "invalid" ? null : error, {
    title: "Не удалось продолжить регистрацию",
    retry: preview.kind === "failed" ? loadPreview : undefined,
  });

  useEffect(() => {
    loadPreview();
    return () => previewRequest.current?.abort();
  }, [loadPreview]);

  useEffect(
    () => () => {
      generation.current += 1;
      request.current?.abort();
      if (providerPending.current && !completed.current) cancelSignIn();
    },
    [cancelSignIn],
  );

  function startRequest() {
    const controller = new AbortController();
    request.current?.abort();
    request.current = controller;
    const id = ++generation.current;
    setBusy(true);
    setError(null);
    clearError();
    return { controller, id };
  }

  async function begin(values: Credentials, hasPassword: boolean) {
    if (preview.kind !== "ready" || busy) return;
    const login = values.login.trim();
    const { controller, id } = startRequest();
    try {
      const response = await staffApi().staff.register.optionsPath.post(
        {
          link: invitation,
          login,
          ...(hasPassword ? { password: values.password } : {}),
          authenticator: true,
          secondFactor: false,
        },
        apiOptions(
          controller.signal,
          "Не удалось начать регистрацию. Проверьте логин и приглашение.",
        ),
      );
      if (id !== generation.current) return;
      setSetup({
        step: "authenticator",
        ...readAuthenticatorSetup(response),
        login,
        hasPassword,
      });
    } catch (issue) {
      if (id === generation.current && !isAbortError(issue)) setError(toApiError(issue).message);
    } finally {
      if (id === generation.current) {
        form.resetField("password");
        form.resetField("confirmation");
        setBusy(false);
      }
    }
  }

  async function skipPassword() {
    if (busy || !passkeysAvailable) return;
    const isLoginValid = await form.trigger("login");
    if (!isLoginValid) return;
    await begin({ login: form.getValues("login"), password: "", confirmation: "" }, false);
  }

  async function confirmAuthenticator(skip: boolean, code?: string) {
    if (preview.kind !== "ready" || setup?.step !== "authenticator" || busy) return;
    if (skip && !canSkipAuthenticator(preview.role, setup.hasPassword)) return;
    if (skip && !passkeysAvailable && !(preview.role === "teacher" && setup.hasPassword)) return;
    const { controller, id } = startRequest();
    try {
      const response = await staffApi().staff.register.authenticator.post(
        {
          challenge: setup.challenge,
          skip,
          ...(!skip ? { code: code ?? "" } : {}),
        },
        apiOptions(controller.signal, "Код не подошёл или настройка устарела."),
      );
      if (id !== generation.current) return;
      const next = readPasskeySetup(response);
      codeForm.reset();
      setSetup({
        step: "passkey",
        ...next,
        login: setup.login,
        hasPassword: setup.hasPassword,
        authenticatorConfirmed: !skip,
      });
    } catch (issue) {
      if (id === generation.current && !isAbortError(issue)) {
        codeForm.reset();
        setSetup(null);
        setError(`${toApiError(issue).message} Начните настройку заново.`);
      }
    } finally {
      if (id === generation.current) setBusy(false);
    }
  }

  async function finish(withPasskey: boolean) {
    if (preview.kind !== "ready" || setup?.step !== "passkey" || busy) return;
    if (
      !withPasskey &&
      !canSkipPasskey(preview.role, setup.hasPassword, setup.authenticatorConfirmed)
    )
      return;
    const { controller, id } = startRequest();
    try {
      // Start WebAuthn before the first await so Safari keeps the user gesture.
      const response = withPasskey
        ? await createPasskeyRegistration(setup.optionsJson, controller.signal)
        : undefined;
      if (id !== generation.current) return;
      providerPending.current = true;
      const accepted = await session.completeRegistration(setup.challenge, undefined, response);
      providerPending.current = false;
      if (id !== generation.current) return;
      setSetup(null);
      if (accepted) {
        completed.current = true;
        onComplete();
      } else {
        setError(
          "Не удалось завершить регистрацию. Начните настройку заново или попробуйте войти.",
        );
      }
    } catch (issue) {
      if (id === generation.current && !isAbortError(issue)) setError(toApiError(issue).message);
    } finally {
      if (id === generation.current) setBusy(false);
    }
  }

  function restart() {
    generation.current += 1;
    request.current?.abort();
    if (providerPending.current) cancelSignIn();
    providerPending.current = false;
    if (setup?.step === "authenticator") codeForm.reset();
    form.resetField("password");
    form.resetField("confirmation");
    setSetup(null);
    setBusy(false);
    setError(null);
    clearError();
  }

  if (preview.kind === "loading")
    return (
      <div role="status" className={styles.loading}>
        <span className={styles.spinner} aria-hidden="true" />
        <span>Проверка приглашения…</span>
      </div>
    );
  if (preview.kind === "invalid")
    return (
      <div className={styles.form}>
        <p className={styles.error} role="alert">
          Приглашение недействительно или уже использовано.
        </p>
        <PaperButton onClick={loadPreview}>Проверить снова</PaperButton>
      </div>
    );

  if (preview.kind === "failed")
    return (
      <div className={styles.form}>
        <PaperButton onClick={loadPreview}>Проверить снова</PaperButton>
      </div>
    );

  const currentStep = !setup ? 0 : setup.step === "authenticator" ? 1 : 2;

  return (
    <div className={styles.form}>
      <ol className={styles.steps} aria-label="Этапы регистрации">
        {steps.map((step, index) => (
          <li
            key={step}
            aria-current={index === currentStep ? "step" : undefined}
            className={index === currentStep ? styles.currentStep : undefined}
          >
            <span className={styles.stepNumber}>{index + 1}</span>
            <span>{step}</span>
            {index === currentStep && <span className={styles.visuallyHidden}>Текущий этап</span>}
          </li>
        ))}
      </ol>
      {!passkeysAvailable && !setup && (
        <p className={styles.info} role="status">
          Ключи доступа недоступны. Используйте пароль и аутентификатор.
        </p>
      )}
      {!setup ? (
        <form
          className={styles.fields}
          onSubmit={form.handleSubmit((values) => begin(values, true))}
          noValidate
        >
          <div className={styles.field}>
            <label htmlFor="registration-login">Логин</label>
            <PaperInput
              id="registration-login"
              autoComplete="username"
              autoCapitalize="none"
              spellCheck={false}
              disabled={busy}
              aria-invalid={Boolean(form.formState.errors.login) || undefined}
              aria-describedby={
                form.formState.errors.login ? "registration-login-error" : undefined
              }
              {...form.register("login", {
                required: "Введите логин до 256 символов.",
                validate: (value) =>
                  (value.trim().length > 0 && value.trim().length <= 256) ||
                  "Введите логин до 256 символов.",
              })}
            />
            {form.formState.errors.login && (
              <span id="registration-login-error" className={styles.fieldError} role="alert">
                {form.formState.errors.login.message}
              </span>
            )}
          </div>
          <div className={styles.field}>
            <label htmlFor="registration-password">Пароль</label>
            <PaperPasswordInput
              id="registration-password"
              autoComplete="new-password"
              disabled={busy}
              aria-invalid={Boolean(form.formState.errors.password) || undefined}
              aria-describedby={
                form.formState.errors.password ? "registration-password-error" : undefined
              }
              {...form.register("password", {
                required: "От 8 до 1024 символов.",
                minLength: { value: 8, message: "От 8 до 1024 символов." },
                maxLength: { value: 1024, message: "От 8 до 1024 символов." },
              })}
            />
            {form.formState.errors.password && (
              <span id="registration-password-error" className={styles.fieldError} role="alert">
                {form.formState.errors.password.message}
              </span>
            )}
          </div>
          <div className={styles.field}>
            <label htmlFor="registration-confirmation">Повторите пароль</label>
            <PaperPasswordInput
              id="registration-confirmation"
              toggleLabel="подтверждение пароля"
              autoComplete="new-password"
              disabled={busy}
              aria-invalid={Boolean(form.formState.errors.confirmation) || undefined}
              aria-describedby={
                form.formState.errors.confirmation ? "registration-confirmation-error" : undefined
              }
              {...form.register("confirmation", {
                required: "Повторите пароль.",
                validate: (value) => value === form.getValues("password") || "Пароли не совпадают.",
              })}
            />
            {form.formState.errors.confirmation && (
              <span id="registration-confirmation-error" className={styles.fieldError} role="alert">
                {form.formState.errors.confirmation.message}
              </span>
            )}
          </div>
          <div className={styles.actions}>
            <PaperButton type="submit" variant="primary" loading={busy}>
              Продолжить
            </PaperButton>
            <PaperButton disabled={busy || !passkeysAvailable} onClick={() => void skipPassword()}>
              Пропустить пароль
            </PaperButton>
          </div>
        </form>
      ) : setup.step === "authenticator" ? (
        <section className={styles.authenticator} aria-labelledby="authenticator-heading">
          <h2 id="authenticator-heading">Добавьте аутентификатор</h2>
          <p>Отсканируйте QR-код в приложении-аутентификаторе или введите ключ вручную.</p>
          <div className={styles.qr}>
            <QRCodeSVG
              value={authenticatorUri(setup.login, setup.authenticatorSecret)}
              size={180}
              bgColor="#ffffff"
              fgColor="#000000"
            />
          </div>
          <div className={styles.field}>
            <label htmlFor="registration-authenticator-secret">Ключ аутентификатора</label>
            <PaperInput
              id="registration-authenticator-secret"
              readOnly
              value={setup.authenticatorSecret}
              autoComplete="off"
            />
          </div>
          <form
            className={styles.fields}
            onSubmit={codeForm.handleSubmit((values) => confirmAuthenticator(false, values.code))}
            noValidate
          >
            <div className={styles.field}>
              <label htmlFor="registration-code">Код из приложения</label>
              <PaperInput
                id="registration-code"
                autoComplete="one-time-code"
                inputMode="numeric"
                maxLength={6}
                disabled={busy}
                aria-invalid={Boolean(codeForm.formState.errors.code) || undefined}
                aria-describedby={
                  codeForm.formState.errors.code ? "registration-code-error" : undefined
                }
                {...codeForm.register("code", {
                  required: "Введите шестизначный код.",
                  pattern: { value: /^\d{6}$/, message: "Введите шестизначный код." },
                })}
              />
              {codeForm.formState.errors.code && (
                <span id="registration-code-error" className={styles.fieldError} role="alert">
                  {codeForm.formState.errors.code.message}
                </span>
              )}
            </div>
            <div className={styles.actions}>
              <PaperButton type="submit" variant="primary" loading={busy}>
                Подтвердить код
              </PaperButton>
              {canSkipAuthenticator(preview.role, setup.hasPassword) && (
                <PaperButton
                  disabled={
                    busy ||
                    (!passkeysAvailable && !(preview.role === "teacher" && setup.hasPassword))
                  }
                  onClick={() => void confirmAuthenticator(true)}
                >
                  Пропустить аутентификатор
                </PaperButton>
              )}
            </div>
          </form>
          <PaperButton className={styles.textButton} onClick={restart}>
            Начать заново
          </PaperButton>
        </section>
      ) : (
        <section className={styles.passkey} aria-labelledby="passkey-heading">
          <h2 id="passkey-heading">Создайте ключ доступа</h2>
          <p>Браузер предложит сохранить ключ на устройстве или в менеджере паролей.</p>
          {!passkeysAvailable && (
            <p className={styles.warning} role="status">
              Ключи доступа недоступны в этом браузере.
            </p>
          )}
          <PaperButton
            variant="primary"
            loading={busy}
            disabled={!passkeysAvailable}
            onClick={() => void finish(true)}
          >
            Создать ключ и войти
          </PaperButton>
          {canSkipPasskey(preview.role, setup.hasPassword, setup.authenticatorConfirmed) && (
            <PaperButton disabled={busy} onClick={() => void finish(false)}>
              Пропустить ключ доступа
            </PaperButton>
          )}
          <PaperButton className={styles.textButton} onClick={restart}>
            Начать заново
          </PaperButton>
        </section>
      )}
    </div>
  );
}
