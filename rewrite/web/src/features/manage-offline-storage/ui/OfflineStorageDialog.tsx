import { Alert, Button, Checkbox, Input, Modal, Space } from "antd";
import { useEffect, useRef, useState } from "react";
import { useOfflineStorage } from "@/entities/offline-storage";

export function OfflineStorageDialog({ open, onClose }: { open: boolean; onClose(): void }) {
  const storage = useOfflineStorage();
  const [secret, setSecret] = useState("");
  const [generated, setGenerated] = useState(false);
  const [saved, setSaved] = useState(false);
  const [generationError, setGenerationError] = useState(false);
  const previousOwner = useRef(storage?.staffId);
  useEffect(() => {
    const ownerChanged = previousOwner.current !== storage?.staffId;
    previousOwner.current = storage?.staffId;
    if (ownerChanged || !open || storage?.status === "unlocked") {
      setSecret("");
      setGenerated(false);
      setSaved(false);
      setGenerationError(false);
    }
  }, [open, storage?.status, storage?.staffId]);
  if (!storage) return null;
  const checking = storage.status === "checking";
  const missing = storage.status === "missing";
  const canUnlock = storage.status === "locked" || missing;

  function generateKey() {
    try {
      const bytes = crypto.getRandomValues(new Uint8Array(32));
      setSecret(Array.from(bytes, (value) => value.toString(16).padStart(2, "0")).join(""));
      setGenerated(true);
      setSaved(false);
      setGenerationError(false);
    } catch {
      setGenerationError(true);
    }
  }

  return (
    <Modal
      open={open}
      title="Локальные данные"
      onCancel={onClose}
      footer={<Button onClick={onClose}>Закрыть</Button>}
      destroyOnHidden
    >
      <Space orientation="vertical" size="middle" style={{ width: "100%" }}>
        <p>
          Черновик нового клиента сохраняется на этом устройстве в зашифрованном виде. На сервер он
          отправляется только кнопкой «Сохранить» в карточке.
        </p>
        {storage.status === "unlocked" ? <Alert type="success" title="Хранилище открыто" /> : null}
        {checking ? <p role="status">Открываем локальные данные…</p> : null}
        {storage.error ? <Alert type="warning" title={storage.error} /> : null}
        {missing ? (
          <>
            <p>
              При входе с паролем хранилище создаётся и открывается автоматически. Для входа без
              пароля сохраните отдельный ключ в надёжном месте: приложение его не запоминает.
            </p>
            <Button onClick={generateKey}>Создать отдельный ключ</Button>
            {generated ? (
              <>
                <Input.TextArea
                  aria-label="Отдельный ключ локальных данных"
                  readOnly
                  value={secret}
                  rows={3}
                  autoComplete="off"
                  spellCheck={false}
                />
                <Checkbox checked={saved} onChange={(event) => setSaved(event.target.checked)}>
                  Ключ сохранён в надёжном месте
                </Checkbox>
              </>
            ) : null}
          </>
        ) : storage.status === "locked" ? (
          <>
            <p>
              Введите пароль, которым хранилище было открыто впервые, или отдельный ключ. После
              сброса пароля для старых данных нужен прежний секрет.
            </p>
            <Input.Password
              aria-label="Пароль или ключ локальных данных"
              autoComplete="off"
              value={secret}
              onChange={(event) => setSecret(event.target.value)}
              onPressEnter={() => {
                if (secret) void storage.unlock(secret);
              }}
            />
          </>
        ) : null}
        {generationError ? (
          <Alert
            type="error"
            title="Не удалось создать ключ. Проверьте доступность защищённого соединения."
          />
        ) : null}
        {canUnlock ? (
          <Button
            type="primary"
            disabled={!secret || (missing && (!generated || !saved))}
            loading={checking}
            onClick={() => void storage.unlock(secret)}
          >
            {missing ? "Включить хранение" : "Открыть хранилище"}
          </Button>
        ) : null}
        <p>
          Выход закрывает доступ во всех вкладках и сохраняет данные. Без пароля или отдельного
          ключа прочитать их нельзя. Шифрование не защищает открытую вкладку от вредоносных
          расширений.
        </p>
      </Space>
    </Modal>
  );
}
