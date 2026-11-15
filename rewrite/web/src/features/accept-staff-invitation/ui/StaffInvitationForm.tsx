import { Alert, Button, Form, Grid, Input, QRCode, Spin, Steps } from "antd";
import { useCallback, useEffect, useRef, useState } from "react";
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
import {
  authenticatorUri,
  canSkipAuthenticator,
  canSkipPasskey,
  readAuthenticatorSetup,
  readPasskeySetup,
} from "../model/registration";
import "./registration.css";

type Preview =
  | { kind: "loading" }
  | { kind: "invalid" }
  | { kind: "ready"; role: StaffIdentity["role"] };
type Setup = (
  | ({ step: "authenticator" } & ReturnType<typeof readAuthenticatorSetup>)
  | ({ step: "passkey"; authenticatorConfirmed: boolean } & ReturnType<typeof readPasskeySetup>)
) & { login: string; hasPassword: boolean };
type Credentials = { login: string; password?: string; confirmation?: string };

export function StaffInvitationForm({
  invitation,
  onComplete,
}: {
  invitation: string;
  onComplete: () => void;
}) {
  const screens = Grid.useBreakpoint();
  const session = useStaffSession();
  const [form] = Form.useForm<Credentials>();
  const [codeForm] = Form.useForm<{ code: string }>();
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
        setPreview({ kind: "invalid" });
        setError(
          apiError.status === 401
            ? "Приглашение недействительно или уже использовано."
            : apiError.message,
        );
      });
  }, [invitation]);

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
    const { controller, id } = startRequest();
    try {
      const response = await staffApi().staff.register.optionsPath.post(
        {
          link: invitation,
          login: values.login.trim(),
          ...(hasPassword ? { password: values.password ?? "" } : {}),
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
        login: values.login.trim(),
        hasPassword,
      });
    } catch (issue) {
      if (id === generation.current && !isAbortError(issue)) setError(toApiError(issue).message);
    } finally {
      if (id === generation.current) {
        form.resetFields(["password", "confirmation"]);
        setBusy(false);
      }
    }
  }

  async function skipPassword() {
    if (busy || !passkeysAvailable) return;
    try {
      const values = await form.validateFields(["login"]);
      await begin({ login: values.login }, false);
    } catch {
      // Form displays its own login validation; password fields are deliberately not validated.
    }
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
      codeForm.resetFields();
      // The confirmed secret and code do not survive the transition to the passkey step.
      setSetup({
        step: "passkey",
        ...next,
        login: setup.login,
        hasPassword: setup.hasPassword,
        authenticatorConfirmed: !skip,
      });
    } catch (issue) {
      if (id === generation.current && !isAbortError(issue)) {
        codeForm.resetFields();
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
      // Server options were prepared by the preceding step; this call preserves Safari's gesture.
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
    if (setup?.step === "authenticator") codeForm.resetFields();
    setSetup(null);
    setBusy(false);
    setError(null);
    clearError();
  }

  if (preview.kind === "loading")
    return (
      <div role="status" className="registration-loading">
        <Spin />
        <span>Проверка приглашения…</span>
      </div>
    );
  if (preview.kind === "invalid")
    return (
      <div className="registration-form">
        <Alert type="error" showIcon title={error ?? "Приглашение недействительно."} />
        <Button onClick={loadPreview}>Проверить снова</Button>
      </div>
    );

  return (
    <div className="registration-form">
      <Steps
        size="small"
        orientation={screens.md ? "horizontal" : "vertical"}
        responsive={false}
        styles={{ itemTitle: { whiteSpace: "nowrap" } }}
        current={!setup ? 0 : setup.step === "authenticator" ? 1 : 2}
        items={[{ title: "Пароль" }, { title: "Аутентификатор" }, { title: "Ключ доступа" }]}
      />
      {error && <Alert type="error" showIcon title={error} />}
      {!passkeysAvailable && !setup && (
        <Alert
          type="info"
          showIcon
          title="Ключи доступа недоступны. Используйте пароль и аутентификатор."
        />
      )}
      {!setup ? (
        <Form
          form={form}
          layout="vertical"
          requiredMark={false}
          onFinish={(values) => void begin(values, true)}
        >
          <Form.Item
            name="login"
            label="Логин"
            rules={[
              {
                required: true,
                whitespace: true,
                max: 256,
                message: "Введите логин до 256 символов.",
              },
            ]}
          >
            <Input
              autoComplete="username"
              autoCapitalize="none"
              spellCheck={false}
              disabled={busy}
            />
          </Form.Item>
          <Form.Item
            name="password"
            label="Пароль"
            rules={[{ required: true, min: 8, max: 1024, message: "От 8 до 1024 символов." }]}
          >
            <Input.Password autoComplete="new-password" disabled={busy} />
          </Form.Item>
          <Form.Item
            name="confirmation"
            label="Повторите пароль"
            dependencies={["password"]}
            rules={[
              { required: true, message: "Повторите пароль." },
              ({ getFieldValue }) => ({
                validator: (_, value: string | undefined) =>
                  value === getFieldValue("password")
                    ? Promise.resolve()
                    : Promise.reject(new Error("Пароли не совпадают.")),
              }),
            ]}
          >
            <Input.Password autoComplete="new-password" disabled={busy} />
          </Form.Item>
          <div className="registration-actions">
            <Button type="primary" htmlType="submit" block loading={busy}>
              Продолжить
            </Button>
            <Button block disabled={busy || !passkeysAvailable} onClick={() => void skipPassword()}>
              Пропустить пароль
            </Button>
          </div>
        </Form>
      ) : setup.step === "authenticator" ? (
        <section className="registration-authenticator" aria-labelledby="authenticator-heading">
          <h2 id="authenticator-heading">Добавьте аутентификатор</h2>
          <p>Отсканируйте QR-код в приложении-аутентификаторе или введите ключ вручную.</p>
          <div className="registration-qr">
            <QRCode
              type="svg"
              value={authenticatorUri(setup.login, setup.authenticatorSecret)}
              size={180}
              color="#000000"
              bgColor="#ffffff"
            />
          </div>
          <label htmlFor="registration-authenticator-secret">Ключ аутентификатора</label>
          <Input
            id="registration-authenticator-secret"
            readOnly
            value={setup.authenticatorSecret}
            autoComplete="off"
          />
          <Form
            form={codeForm}
            layout="vertical"
            onFinish={(values) => void confirmAuthenticator(false, values.code)}
          >
            <Form.Item
              name="code"
              label="Код из приложения"
              rules={[{ required: true, pattern: /^\d{6}$/, message: "Введите шестизначный код." }]}
            >
              <Input
                autoComplete="one-time-code"
                inputMode="numeric"
                maxLength={6}
                disabled={busy}
              />
            </Form.Item>
            <div className="registration-actions">
              <Button type="primary" htmlType="submit" block loading={busy}>
                Подтвердить код
              </Button>
              {canSkipAuthenticator(preview.role, setup.hasPassword) && (
                <Button
                  block
                  disabled={
                    busy ||
                    (!passkeysAvailable && !(preview.role === "teacher" && setup.hasPassword))
                  }
                  onClick={() => void confirmAuthenticator(true)}
                >
                  Пропустить аутентификатор
                </Button>
              )}
            </div>
          </Form>
          <Button type="text" onClick={restart}>
            Начать заново
          </Button>
        </section>
      ) : (
        <section className="registration-passkey" aria-labelledby="passkey-heading">
          <h2 id="passkey-heading">Создайте ключ доступа</h2>
          <p>Браузер предложит сохранить ключ на устройстве или в менеджере паролей.</p>
          {!passkeysAvailable && (
            <Alert type="warning" showIcon title="Ключи доступа недоступны в этом браузере." />
          )}
          <Button
            type="primary"
            block
            loading={busy}
            disabled={!passkeysAvailable}
            onClick={() => void finish(true)}
          >
            Создать ключ и войти
          </Button>
          {canSkipPasskey(preview.role, setup.hasPassword, setup.authenticatorConfirmed) && (
            <Button block disabled={busy} onClick={() => void finish(false)}>
              Пропустить ключ доступа
            </Button>
          )}
          <Button type="text" onClick={restart}>
            Начать заново
          </Button>
        </section>
      )}
    </div>
  );
}
