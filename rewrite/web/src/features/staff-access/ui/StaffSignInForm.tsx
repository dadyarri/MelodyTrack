import { Alert, Button, Form, Input, Space, Tabs, Typography } from "antd";
import { useState } from "react";
import { useStaffSession } from "@/entities/session";
import { supportsPasskeys } from "@/shared/lib/passkeys";
import "./StaffSignInForm.css";

type PrimaryValues = { login: string; password?: string };
type FactorValues = { code: string };

export function StaffSignInForm() {
  const session = useStaffSession();
  const [mode, setMode] = useState<"password" | "passkey">("password");
  const [primaryForm] = Form.useForm<PrimaryValues>();
  const [factorForm] = Form.useForm<FactorValues>();
  const busy = session.status === "authenticating";

  const submitPrimary = (values: PrimaryValues) => {
    const login = values.login.trim();
    if (mode === "passkey") {
      // Form validation may complete asynchronously. The prepared assertion is
      // started only by the direct confirmation-button gesture below.
      if (session.passkeyReady) return;
      void session.signInWithPasskey(login);
    } else {
      const password = values.password ?? "";
      primaryForm.resetFields(["password"]);
      void session.signInWithPassword(login, password);
    }
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
            Введите текущий шестизначный код из приложения-аутентификатора.
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
              disabled={busy || !supportsPasskeys()}
              className="staff-access-secondary"
              onClick={() => {
                factorForm.resetFields();
                void session.completeFactorWithPasskey();
              }}
            >
              Подтвердить ключом доступа
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
          <Tabs
            activeKey={mode}
            onChange={(key) => {
              if (key !== "password" && key !== "passkey") return;
              session.cancelSignIn();
              primaryForm.resetFields(["password"]);
              setMode(key);
            }}
            items={[
              { key: "password", label: "По паролю", disabled: busy },
              { key: "passkey", label: "Ключ доступа", disabled: busy },
            ]}
          />
          <Form<PrimaryValues>
            form={primaryForm}
            layout="vertical"
            requiredMark={false}
            onFinish={submitPrimary}
            disabled={busy}
            onValuesChange={(changed: Partial<PrimaryValues>) => {
              if (changed.login !== undefined && session.passkeyReady) session.cancelSignIn();
            }}
          >
            <Form.Item
              name="login"
              label="Логин сотрудника"
              rules={[{ required: true, whitespace: true, message: "Введите ваш логин." }]}
            >
              <Input
                autoComplete="username"
                autoCapitalize="none"
                autoCorrect="off"
                spellCheck={false}
                placeholder="Ваш логин"
                size="large"
              />
            </Form.Item>
            {mode === "password" && (
              <Form.Item
                name="password"
                label="Пароль"
                rules={[{ required: true, message: "Введите пароль." }]}
              >
                <Input.Password
                  autoComplete="current-password"
                  placeholder="Введите пароль"
                  size="large"
                />
              </Form.Item>
            )}
            {mode === "passkey" && (
              <Typography.Paragraph type="secondary">
                {session.passkeyReady
                  ? "Ключ готов. Нажмите подтверждение и следуйте подсказкам вашего устройства."
                  : "Продолжите, чтобы подготовить вход с ключом на этом устройстве или телефоне."}
              </Typography.Paragraph>
            )}
            {mode === "passkey" && !supportsPasskeys() && (
              <Alert
                type="info"
                showIcon
                title="Ключи доступа здесь недоступны"
                description="Откройте приложение по HTTPS или используйте вход по паролю."
              />
            )}
            <Button
              type="primary"
              htmlType={mode === "passkey" && session.passkeyReady ? "button" : "submit"}
              size="large"
              block
              loading={busy}
              disabled={mode === "passkey" && !supportsPasskeys()}
              onClick={() => {
                if (mode !== "passkey" || !session.passkeyReady) return;
                const login: unknown = primaryForm.getFieldValue("login");
                if (typeof login === "string" && login.trim()) {
                  void session.signInWithPasskey(login.trim());
                }
              }}
            >
              {mode === "password"
                ? "Войти"
                : session.passkeyReady
                  ? "Подтвердить ключом доступа"
                  : "Продолжить с ключом доступа"}
            </Button>
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
          </Form>
          <Space orientation="vertical" size={4} className="staff-access-help">
            <Typography.Text type="secondary">Первый вход или потеряли доступ?</Typography.Text>
            <Typography.Text type="secondary">
              Попросите у суперпользователя приглашение или ссылку восстановления.
            </Typography.Text>
          </Space>
        </>
      )}
    </div>
  );
}
