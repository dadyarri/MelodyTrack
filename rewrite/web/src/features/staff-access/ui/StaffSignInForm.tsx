import { Alert, Button, Form, Input, Typography } from "antd";
import { useEffect, useState } from "react";
import { useStaffSession } from "@/entities/session";
import { supportsPasskeys } from "@/shared/lib/passkeys";
import "./StaffSignInForm.css";

type PrimaryValues = { login: string; password?: string };
type FactorValues = { code: string };

export function StaffSignInForm() {
  const session = useStaffSession();
  const [method, setMethod] = useState<"password" | "passkey">("password");
  const [primaryForm] = Form.useForm<PrimaryValues>();
  const [factorForm] = Form.useForm<FactorValues>();
  const busy = session.status === "authenticating";
  const passkeysSupported = supportsPasskeys();
  const { preparePasskey, factor } = session;

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
    const password = values.password ?? "";
    primaryForm.resetFields(["password"]);
    void session.signInWithPassword(login, password);
  };

  const submitFactor = (values: FactorValues) => {
    const code = values.code;
    factorForm.resetFields();
    void session.completeFactor(code);
  };

  return (
    <div className="staff-access-form">
      {session.error && (
        <Alert
          type="error"
          showIcon
          title={session.error.message}
          description={
            <>
              {session.error.retryAfterSeconds && (
                <p>Повторите через {session.error.retryAfterSeconds} сек.</p>
              )}
              {session.error.traceId && (
                <p className="staff-access-trace">
                  Код обращения: <code>{session.error.traceId}</code>
                </p>
              )}
            </>
          }
          closable
          onClose={() => session.clearError()}
        />
      )}

      {session.factor ? (
        <section aria-labelledby="staff-access-factor-title">
          <Typography.Title level={3} id="staff-access-factor-title">
            Подтвердите вход
          </Typography.Title>
          <Typography.Paragraph type="secondary">
            Введите код из приложения-аутентификатора.
          </Typography.Paragraph>
          <Form<FactorValues>
            form={factorForm}
            layout="vertical"
            requiredMark={false}
            onFinish={submitFactor}
            disabled={busy}
          >
            <Form.Item
              name="code"
              label="Код подтверждения"
              rules={[
                { required: true, message: "Введите код из приложения-аутентификатора." },
                { pattern: /^\d{6}$/, message: "Код состоит из шести цифр." },
              ]}
            >
              <Input
                autoComplete="one-time-code"
                inputMode="numeric"
                maxLength={6}
                autoFocus
                placeholder="000000"
                size="large"
              />
            </Form.Item>
            <Button type="primary" htmlType="submit" block size="large" loading={busy}>
              Подтвердить вход
            </Button>
          </Form>
          {session.factor.canUsePasskey && (
            <Button
              block
              size="large"
              disabled={busy || !passkeysSupported}
              className="staff-access-secondary"
              onClick={() => {
                factorForm.resetFields();
                void session.completeFactorWithPasskey();
              }}
            >
              Использовать ключ доступа
            </Button>
          )}
          <Button
            type="link"
            onClick={() => {
              factorForm.resetFields();
              session.cancelSignIn();
            }}
          >
            Начать вход заново
          </Button>
        </section>
      ) : (
        <>
          <Form<PrimaryValues>
            form={primaryForm}
            layout="vertical"
            requiredMark={false}
            onFinish={submitPrimary}
            disabled={busy}
          >
            <Form.Item
              name="login"
              label="Логин"
              rules={[{ required: true, whitespace: true, message: "Введите ваш логин." }]}
            >
              <Input
                autoComplete="username"
                autoCapitalize="none"
                autoCorrect="off"
                spellCheck={false}
                size="large"
              />
            </Form.Item>
            <Form.Item
              name="password"
              label="Пароль"
              rules={[{ required: true, message: "Введите пароль." }]}
            >
              <Input.Password autoComplete="current-password" size="large" />
            </Form.Item>
            <Button
              type="primary"
              htmlType="submit"
              size="large"
              block
              loading={busy && method === "password"}
            >
              Войти
            </Button>
          </Form>
          <section className="staff-access-passkey" aria-label="Вход с ключом доступа">
            {!passkeysSupported && (
              <Alert
                type="info"
                showIcon
                title="Ключи доступа здесь недоступны"
                description="Откройте приложение по HTTPS или используйте вход по паролю."
              />
            )}
            <Button
              htmlType="button"
              size="large"
              block
              loading={session.preparingPasskey || (busy && method === "passkey")}
              aria-label="Войти с ключом"
              disabled={busy || !session.passkeyReady || !passkeysSupported}
              onClick={() => {
                setMethod("passkey");
                primaryForm.resetFields(["password"]);
                void session.signInWithPasskey();
              }}
            >
              Войти с ключом
            </Button>
            {passkeysSupported &&
              !session.passkeyReady &&
              !session.preparingPasskey &&
              session.error && (
                <Button
                  block
                  className="staff-access-secondary"
                  onClick={() => void preparePasskey()}
                >
                  Повторить
                </Button>
              )}
          </section>
          {busy && (
            <Button
              type="link"
              block
              onClick={() => {
                primaryForm.resetFields(["password"]);
                session.cancelSignIn();
              }}
            >
              Отменить попытку
            </Button>
          )}
        </>
      )}
    </div>
  );
}
