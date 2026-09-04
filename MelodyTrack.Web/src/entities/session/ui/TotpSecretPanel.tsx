import { Alert, App as AntdApp, Button, Form, Input, QRCode, Space } from "antd";
import type { ReactNode } from "react";

import { copyTextToClipboard } from "@/shared/lib";
import { authScreenStyles as styles } from "@/shared/ui";
import { CopyOutlined } from "@/shared/ui/icons";

type TotpSecretPanelProps = {
  alertType: "info" | "warning";
  alertMessage: string;
  alertDescription: string;
  qrValue: string;
  secret: string;
  qrSize?: number;
  copyable?: boolean;
  children?: ReactNode;
};

export function TotpSecretPanel({
  alertType,
  alertMessage,
  alertDescription,
  qrValue,
  secret,
  qrSize = 200,
  copyable = false,
  children,
}: TotpSecretPanelProps) {
  const { message } = AntdApp.useApp();

  function copySecret() {
    void copyTextToClipboard(secret).then((copied) => {
      if (copied) {
        void message.success("Секрет скопирован.");
        return;
      }

      void message.error("Не удалось скопировать секрет. Выделите его и скопируйте вручную.");
    });
  }

  return (
    <Space orientation="vertical" size={16} className="wide">
      <Alert type={alertType} showIcon title={alertMessage} description={alertDescription} />
      <div className={styles.totpQr}>
        <QRCode value={qrValue} size={qrSize} />
      </div>
      <Form.Item label="Секрет для ручного ввода" className={styles.compactFormItem}>
        <Input
          readOnly
          value={secret}
          suffix={
            copyable ? <Button type="text" icon={<CopyOutlined />} aria-label="Скопировать секрет" onClick={copySecret} /> : undefined
          }
        />
      </Form.Item>
      {children}
    </Space>
  );
}
