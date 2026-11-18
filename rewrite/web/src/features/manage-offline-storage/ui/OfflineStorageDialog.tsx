import { useEffect, useRef, useState } from "react";
import { useOfflineStorage } from "@/entities/offline-storage";
import { Icon } from "@/shared/ui/Icon";
import { PaperButton, PaperDialog, PaperInput } from "@/shared/ui/paper";
import styles from "./OfflineStorageDialog.module.css";

export function OfflineStorageDialog({ open, onClose }: { open: boolean; onClose(): void }) {
  const storage = useOfflineStorage();
  const [secret, setSecret] = useState("");
  const [generated, setGenerated] = useState(false);
  const [saved, setSaved] = useState(false);
  const [secretVisible, setSecretVisible] = useState(false);
  const [generationError, setGenerationError] = useState(false);
  const previousOwner = useRef(storage?.staffId);
  useEffect(() => {
    const ownerChanged = previousOwner.current !== storage?.staffId;
    previousOwner.current = storage?.staffId;
    if (ownerChanged || !open || storage?.status === "unlocked") {
      setSecret("");
      setGenerated(false);
      setSaved(false);
      setSecretVisible(false);
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
    <PaperDialog
      open={open}
      title="Локальные данные"
      onClose={onClose}
      footer={<PaperButton onClick={onClose}>Закрыть</PaperButton>}
      className={styles.dialog}
    >
      <div className={styles.content}>
        <p>
          Черновик нового клиента сохраняется на этом устройстве в зашифрованном виде. На сервер он
          отправляется только кнопкой «Сохранить» в карточке.
        </p>
        {storage.status === "unlocked" ? (
          <p className={styles.success} role="status">
            Хранилище открыто
          </p>
        ) : null}
        {checking ? <p role="status">Открываем локальные данные…</p> : null}
        {storage.error ? (
          <p className={styles.warning} role="alert">
            {storage.error}
          </p>
        ) : null}
        {missing ? (
          <>
            <p>
              При входе с паролем хранилище создаётся и открывается автоматически. Для входа без
              пароля сохраните отдельный ключ в надёжном месте: приложение его не запоминает.
            </p>
            <PaperButton onClick={generateKey}>Создать отдельный ключ</PaperButton>
            {generated ? (
              <>
                <textarea
                  aria-label="Отдельный ключ локальных данных"
                  className={styles.generatedSecret}
                  readOnly
                  value={secret}
                  rows={3}
                  autoComplete="off"
                  spellCheck={false}
                />
                <label className={styles.savedConfirmation}>
                  <input
                    type="checkbox"
                    checked={saved}
                    onChange={(event) => setSaved(event.target.checked)}
                  />
                  <span>Ключ сохранён в надёжном месте</span>
                </label>
              </>
            ) : null}
          </>
        ) : storage.status === "locked" ? (
          <>
            <p>
              Введите пароль, которым хранилище было открыто впервые, или отдельный ключ. После
              сброса пароля для старых данных нужен прежний секрет.
            </p>
            <div className={styles.secretInputRow}>
              <PaperInput
                aria-label="Пароль или ключ локальных данных"
                type={secretVisible ? "text" : "password"}
                autoComplete="off"
                value={secret}
                onChange={(event) => setSecret(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === "Enter" && secret) void storage.unlock(secret);
                }}
              />
              <PaperButton
                size="small"
                aria-label={secretVisible ? "Скрыть пароль или ключ" : "Показать пароль или ключ"}
                title={secretVisible ? "Скрыть пароль или ключ" : "Показать пароль или ключ"}
                aria-pressed={secretVisible}
                icon={<Icon name={secretVisible ? "eyeOff" : "eye"} />}
                onClick={() => setSecretVisible((visible) => !visible)}
              />
            </div>
          </>
        ) : null}
        {generationError ? (
          <p className={styles.error} role="alert">
            Не удалось создать ключ. Проверьте доступность защищённого соединения.
          </p>
        ) : null}
        {canUnlock ? (
          <PaperButton
            variant="primary"
            disabled={!secret || (missing && (!generated || !saved))}
            loading={checking}
            onClick={() => void storage.unlock(secret)}
          >
            {missing ? "Включить хранение" : "Открыть хранилище"}
          </PaperButton>
        ) : null}
        <p>
          Выход закрывает доступ во всех вкладках и сохраняет данные. Без пароля или отдельного
          ключа прочитать их нельзя. Шифрование не защищает открытую вкладку от вредоносных
          расширений.
        </p>
      </div>
    </PaperDialog>
  );
}
