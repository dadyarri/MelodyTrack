import { Button, Card, Form, Input, Segmented, Space, Typography } from "antd";
import { Navigate } from "react-router";

import type { LoginInput, RecoveryCodeItem } from "@/entities/session";
import { RecoveryCodesCard, TotpSecretPanel } from "@/entities/session";
import { SystemNoticeCenter } from "@/entities/system-notice";
import { type AuthMode, type SecondFactorMode, useAuthPageController } from "@/features/auth";
import { AuthScreenLayout, authScreenStyles as authStyles, StatusBanner } from "@/shared/ui";
import { KeyOutlined, LockOutlined, MailOutlined, UserOutlined } from "@/shared/ui/icons";

export function AuthPage() {
  const controller = useAuthPageController();

  if (controller.auth.isLoading && !controller.recover2FaState) {
    return null;
  }

  if (controller.auth.isAuthenticated && !controller.recover2FaState) {
    return <Navigate to={controller.auth.user?.isClientPortal ? "/portal" : "/"} replace />;
  }

  return (
    <AuthScreenLayout title="MelodyTrack">
      <SystemNoticeCenter preAuth />
      {controller.totpSetup ||
      controller.recoveryCodes ||
      controller.recover2FaState ||
      controller.hasInviteCode ||
      controller.loginChallenge ? null : (
        <Segmented<AuthMode>
          block
          value={controller.mode}
          onChange={controller.setMode}
          options={[
            { label: "Вход", value: "login" },
            { label: "Сброс 2FA", value: "recover2fa" },
          ]}
        />
      )}
      {controller.totpSetup ? (
        <TotpSecretPanel
          alertType="info"
          alertMessage="Настройка 2FA"
          alertDescription="Отсканируйте QR-код или введите секрет вручную в Bitwarden/Authy/Google Authenticator. Пока вы не подтвердите код и не сохраните коды восстановления, вход для этой учетной записи не будет завершен."
          qrValue={controller.totpSetup.otpUrl}
          secret={controller.totpSetup.secret}
          copyable
        >
          <Form<{ otp: string }> layout="vertical" onFinish={controller.onVerify2FaSubmit} requiredMark={false}>
            <Form.Item name="otp" label="Код 2FA" rules={[{ required: true }]}>
              <Input inputMode="numeric" autoComplete="one-time-code" autoFocus />
            </Form.Item>
            <Button block type="primary" htmlType="submit" loading={controller.verify2FaMutation.isPending}>
              Подтвердить 2FA
            </Button>
          </Form>
        </TotpSecretPanel>
      ) : controller.recover2FaState ? (
        <TotpSecretPanel
          alertType="warning"
          alertMessage="Доступ к 2FA восстановлен"
          alertDescription="Добавьте новый секрет в приложение-аутентификатор и сохраните новые коды восстановления. Старые коды больше не действуют."
          qrValue={controller.recover2FaState.otpUrl}
          secret={controller.recover2FaState.secret}
          copyable
        >
          <RecoveryCodesCard
            items={controller.recover2FaState.allCodes}
            downloadFileName={`MelodyTrackRecovery_${toRecoveryFileStem(controller.recoveryCodeUsername)}.txt`}
            description="Эти коды замещают старые. Каждый код одноразовый."
          />
          <Button block type="primary" onClick={controller.continueAfterRecovered2Fa}>
            Продолжить
          </Button>
        </TotpSecretPanel>
      ) : controller.recoveryCodes ? (
        <RecoveryCodesBlock
          codes={controller.recoveryCodes}
          username={controller.recoveryCodeUsername}
          onContinue={controller.continueAfterRecoveryCodes}
        />
      ) : controller.hasInviteCode ? (
        <Form
          form={controller.registerForm}
          layout="vertical"
          initialValues={{
            inviteCode: controller.inviteCode,
            email: controller.inviteEmail,
          }}
          onFinish={controller.onRegisterSubmit}
          requiredMark={false}
        >
          <StatusBanner type="info" title="Регистрация доступна только по ссылке-приглашению." />
          {controller.inviteQuery.isPending ? <StatusBanner type="info" title="Проверяем ссылку приглашения..." /> : null}
          {controller.inviteErrorMessage ? <StatusBanner type="error" title={controller.inviteErrorMessage} /> : null}
          {controller.canSubmitRegistration && controller.inviteLookupFinished ? (
            <StatusBanner
              type="warning"
              title="Если для вашей роли обязателен 2FA, после регистрации нужно сразу подтвердить код из приложения и сохранить коды восстановления."
            />
          ) : null}
          <Form.Item name="inviteCode" label="Код приглашения" rules={[{ required: true }]}>
            <Input disabled />
          </Form.Item>
          <Form.Item name="email" label="Email" rules={[{ required: true }, { type: "email" }]}>
            <Input prefix={<MailOutlined />} disabled={Boolean(controller.inviteEmail)} />
          </Form.Item>
          <Form.Item name="firstName" label="Имя" rules={[{ required: true }]}>
            <Input prefix={<UserOutlined />} />
          </Form.Item>
          <Form.Item name="lastName" label="Фамилия" rules={[{ required: true }]}>
            <Input prefix={<UserOutlined />} />
          </Form.Item>
          <Form.Item name="password" label="Пароль" rules={[{ required: true }]}>
            <Input.Password prefix={<LockOutlined />} autoComplete="new-password" />
          </Form.Item>
          <Button
            block
            type="primary"
            htmlType="submit"
            loading={controller.registerMutation.isPending}
            disabled={!controller.canSubmitRegistration}
          >
            Зарегистрироваться
          </Button>
        </Form>
      ) : controller.mode === "login" ? (
        <Space orientation="vertical" size={16} className="wide">
          {controller.loginChallenge ? (
            <Space orientation="vertical" size={16} className="wide">
              {controller.loginChallenge.canUseOtp && controller.loginChallenge.canUseRecoveryCode ? (
                <Segmented<SecondFactorMode>
                  block
                  value={controller.loginSecondFactorMode}
                  onChange={controller.onLoginSecondFactorModeChange}
                  options={[
                    { label: "Код 2FA", value: "otp" },
                    { label: "Код восстановления", value: "recoveryCode" },
                  ]}
                />
              ) : null}
              <Form<Pick<LoginInput, "otp" | "recoveryCode">>
                form={controller.secondFactorForm}
                layout="vertical"
                onFinish={controller.onLoginSecondFactorSubmit}
                requiredMark={false}
              >
                {controller.loginSecondFactorMode === "otp" ? (
                  <Form.Item
                    name="otp"
                    label="Код 2FA"
                    rules={[
                      { required: true, message: "Введите код 2FA" },
                      { len: 6, message: "Код должен содержать 6 цифр" },
                    ]}
                  >
                    <CharacterCodeInput length={6} mode="numeric" autoFocus />
                  </Form.Item>
                ) : (
                  <Form.Item
                    name="recoveryCode"
                    label="Код восстановления"
                    rules={[
                      { required: true, message: "Введите код восстановления" },
                      { len: 10, message: "Код должен содержать 10 символов" },
                    ]}
                  >
                    <CharacterCodeInput length={10} mode="alphanumeric" />
                  </Form.Item>
                )}
                <Space orientation="vertical" size={12} className="wide">
                  <Button
                    block
                    type="primary"
                    htmlType="submit"
                    loading={controller.loginSecondFactorMutation.isPending}
                    disabled={controller.loginSecondFactorMutation.isPending}
                  >
                    Подтвердить вход
                  </Button>
                  <Button block onClick={controller.resetLoginChallenge}>
                    Назад
                  </Button>
                </Space>
              </Form>
            </Space>
          ) : (
            <>
              <Form<Pick<LoginInput, "email" | "password">>
                form={controller.loginForm}
                layout="vertical"
                onFinish={controller.onLoginSubmit}
                requiredMark={false}
              >
                <Form.Item name="email" label="Email" rules={[{ required: true }, { type: "email" }]}>
                  <Input prefix={<MailOutlined />} autoComplete="email" />
                </Form.Item>
                <Form.Item name="password" label="Пароль" rules={[{ required: true }]}>
                  <Input.Password prefix={<LockOutlined />} autoComplete="current-password" />
                </Form.Item>
                <Button
                  block
                  type="primary"
                  htmlType="submit"
                  loading={controller.loginMutation.isPending}
                  disabled={controller.loginMutation.isPending}
                >
                  Продолжить
                </Button>
              </Form>
              {controller.passwordResetGuidanceVisible ? (
                <StatusBanner
                  type="warning"
                  title="Не удается войти после обновления безопасности?"
                  description="Если пароль не менялся после обновления безопасности, его необходимо сбросить. Обратитесь к администратору за ссылкой восстановления."
                />
              ) : (
                <div className={authStyles.authSupportNote}>
                  <Typography.Text strong>Нужен сброс пароля?</Typography.Text>
                  <Typography.Paragraph type="secondary" className={authStyles.authSupportNoteText}>
                    Обратитесь к администратору, чтобы получить ссылку для восстановления пароля.
                  </Typography.Paragraph>
                </div>
              )}
            </>
          )}
        </Space>
      ) : controller.mode === "recover2fa" ? (
        <Form form={controller.recover2FaForm} layout="vertical" onFinish={controller.onRecover2FaSubmit} requiredMark={false}>
          <Typography.Text strong>Потеряли доступ к приложению-аутентификатору?</Typography.Text>
          <Typography.Paragraph type="secondary" className={authStyles.helperText}>
            Введите email и один из сохраненных кодов восстановления. После этого вы получите новый секрет и новый набор кодов.
          </Typography.Paragraph>
          <Form.Item name="email" label="Email" rules={[{ required: true }, { type: "email" }]}>
            <Input prefix={<MailOutlined />} autoComplete="email" />
          </Form.Item>
          <Form.Item name="recoveryCode" label="Код восстановления" rules={[{ required: true }]}>
            <Input prefix={<KeyOutlined />} autoComplete="one-time-code" />
          </Form.Item>
          <Button block type="primary" htmlType="submit" loading={controller.recover2FaMutation.isPending}>
            Восстановить доступ
          </Button>
        </Form>
      ) : (
        <Card size="small">
          <Typography.Text type="secondary">Неверное состояние экрана входа.</Typography.Text>
        </Card>
      )}
    </AuthScreenLayout>
  );
}

function RecoveryCodesBlock({ codes, username, onContinue }: { codes: RecoveryCodeItem[]; username: string; onContinue: () => void }) {
  return (
    <Space orientation="vertical" size={16} className="wide">
      <RecoveryCodesCard
        items={codes}
        downloadFileName={`MelodyTrackRecovery_${toRecoveryFileStem(username)}.txt`}
        description="Каждый код одноразовый. Они пригодятся, если у вас не будет доступа к приложению-аутентификатору."
      />
      <Button block type="primary" onClick={onContinue}>
        Перейти ко входу
      </Button>
    </Space>
  );
}

function toRecoveryFileStem(value: string) {
  const stem = value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9._-]+/g, "_");
  return stem || "user";
}

function CharacterCodeInput({
  length,
  mode,
  value,
  onChange,
  autoFocus = false,
}: {
  length: number;
  mode: "numeric" | "alphanumeric";
  value?: string;
  onChange?: (value: string) => void;
  autoFocus?: boolean;
}) {
  return (
    <Input.OTP
      length={length}
      value={value}
      autoFocus={autoFocus}
      mask={false}
      inputMode={mode === "numeric" ? "numeric" : "text"}
      formatter={(next) => (mode === "numeric" ? next.replace(/\D/g, "") : next.toUpperCase().replace(/[^A-Z0-9]/g, ""))}
      onChange={(next) => {
        onChange?.(mode === "numeric" ? next.replace(/\D/g, "") : next.toUpperCase().replace(/[^A-Z0-9]/g, ""));
      }}
    />
  );
}
