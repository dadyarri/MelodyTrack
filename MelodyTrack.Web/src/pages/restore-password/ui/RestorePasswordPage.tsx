import { Button, Form, Input, Segmented } from "antd";

import type { ResetPasswordInput } from "@/entities/session";
import { type ResetPasswordSecondFactorMode, useRestorePasswordPageController } from "@/features/auth";
import { AuthScreenLayout } from "@/shared/ui";
import { StatusBanner } from "@/shared/ui";
import { KeyOutlined, LockOutlined, SafetyCertificateOutlined } from "@/shared/ui/icons";

export function RestorePasswordPage() {
  const controller = useRestorePasswordPageController();

  return (
    <AuthScreenLayout title="Восстановление пароля" description="Если 2FA включен, используйте одноразовый код или код восстановления.">
      {!controller.token ? <StatusBanner type="error" title="В ссылке нет токена восстановления." /> : null}
      {controller.token ? <StatusBanner type="info" title="Если ссылка уже использована или просрочена, запросите новую ссылку." /> : null}
      {controller.submitError ? <StatusBanner type="error" title={controller.submitError} /> : null}
      <Form<Omit<ResetPasswordInput, "token">> layout="vertical" onFinish={controller.onSubmit} requiredMark={false}>
        <Form.Item name="newPassword" label="Новый пароль" rules={[{ required: true }]}>
          <Input.Password prefix={<LockOutlined />} autoComplete="new-password" />
        </Form.Item>
        <Segmented<ResetPasswordSecondFactorMode>
          block
          value={controller.secondFactorMode}
          onChange={controller.setSecondFactorMode}
          options={[
            { label: "Код 2FA", value: "otp" },
            { label: "Код восстановления", value: "recoveryCode" },
          ]}
        />
        {controller.secondFactorMode === "otp" ? (
          <Form.Item name="otp" label="Код 2FA">
            <Input prefix={<SafetyCertificateOutlined />} inputMode="numeric" autoComplete="one-time-code" />
          </Form.Item>
        ) : (
          <Form.Item name="recoveryCode" label="Код восстановления">
            <Input prefix={<KeyOutlined />} autoComplete="one-time-code" />
          </Form.Item>
        )}
        <Button block type="primary" htmlType="submit" disabled={!controller.token} loading={controller.resetPasswordMutation.isPending}>
          Сменить пароль
        </Button>
      </Form>
    </AuthScreenLayout>
  );
}
