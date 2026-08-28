import { App as AntdApp, Button, Card, Space, Typography } from "antd";

import { copyTextToClipboard } from "@/shared/lib";
import { authScreenStyles as styles } from "@/shared/ui";
import { CopyOutlined, DownloadOutlined } from "@/shared/ui/icons";

import type { RecoveryCodeItem } from "../api/sessionApi";

interface RecoveryCodesCardProps {
  items: RecoveryCodeItem[];
  title?: string;
  description?: string;
  downloadFileName: string;
}

export function RecoveryCodesCard({
  items,
  title = "Коды восстановления",
  description = "Сохраните эти коды в надежном месте. Использованные коды остаются в списке, но не попадают в копирование и экспорт.",
  downloadFileName,
}: RecoveryCodesCardProps) {
  const { message } = AntdApp.useApp();
  const activeCodes = items.filter((item) => !item.wasUsed).map((item) => item.code);

  function copyCodes() {
    if (activeCodes.length === 0) {
      message.warning("Не осталось активных кодов для копирования.");
      return;
    }

    void copyTextToClipboard(activeCodes.join("\n")).then((copied) => {
      if (copied) {
        void message.success("Активные коды восстановления скопированы.");
        return;
      }

      void message.error("Не удалось скопировать коды. Выделите их и скопируйте вручную.");
    });
  }

  function downloadCodes() {
    if (activeCodes.length === 0) {
      message.warning("Не осталось активных кодов для экспорта.");
      return;
    }

    const blob = new Blob([activeCodes.join("\n")], {
      type: "text/plain;charset=utf-8",
    });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = downloadFileName;
    anchor.click();
    URL.revokeObjectURL(url);
    message.success("TXT с активными кодами подготовлен.");
  }

  return (
    <Card
      title={title}
      extra={
        <Space wrap>
          <Button icon={<CopyOutlined />} onClick={copyCodes}>
            Копировать активные
          </Button>
          <Button icon={<DownloadOutlined />} onClick={downloadCodes}>
            Скачать TXT
          </Button>
        </Space>
      }
    >
      <Space orientation="vertical" className="wide" size={14}>
        <Typography.Text type="secondary">{description}</Typography.Text>
        <div className={styles.recoveryCodesGrid}>
          {items.map((item) => (
            <Typography.Text
              key={item.code}
              code
              className={item.wasUsed ? `${styles.recoveryCode} ${styles.recoveryCodeUsed}` : styles.recoveryCode}
            >
              {item.code}
            </Typography.Text>
          ))}
        </div>
      </Space>
    </Card>
  );
}
