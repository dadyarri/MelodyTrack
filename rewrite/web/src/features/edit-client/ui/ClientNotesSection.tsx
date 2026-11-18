import { useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { Icon } from "@/shared/ui/Icon";
import { useErrorNotification } from "@/shared/ui/notifications";
import { PaperButton, PaperTooltip } from "@/shared/ui/paper";
import styles from "./client-editor.module.css";

export type ClientNoteHistoryEntry = {
  id: string;
  authorName: string;
  changedAt: string;
  before: string | null;
  after: string | null;
};

type ClientNotesSectionProps = {
  notes: string | null;
  history: ClientNoteHistoryEntry[];
  canEdit: boolean;
  loading?: boolean;
  saving?: boolean;
  error?: string | null;
  resetKey?: string | number;
  onSave: (notes: string | null, expectedNotes: string | null) => void;
  onReload: () => void;
};

const dateTime = new Intl.DateTimeFormat("ru-RU", {
  timeZone: "Europe/Moscow",
  dateStyle: "medium",
  timeStyle: "short",
});

export function ClientNotesSection({
  notes,
  history,
  canEdit,
  loading = false,
  saving = false,
  error = null,
  resetKey = 0,
  onSave,
  onReload,
}: ClientNotesSectionProps) {
  const [initial, setInitial] = useState(notes);
  const [value, setValue] = useState(notes ?? "");
  const [historyOpen, setHistoryOpen] = useState(false);
  const historyId = useId();
  const notesInput = useRef<HTMLTextAreaElement>(null);
  const previousResetKey = useRef(resetKey);
  const changed = (value || null) !== initial;
  const stale = changed && notes !== initial;
  const conflict = error?.startsWith("Заметки изменились на сервере.") === true;

  useErrorNotification(conflict ? null : error, { title: "Ошибка при работе с заметками" });

  useEffect(() => {
    const reset = previousResetKey.current !== resetKey;
    previousResetKey.current = resetKey;
    if (changed && !reset) return;
    setInitial(notes);
    setValue(notes ?? "");
  }, [changed, notes, resetKey]);

  useLayoutEffect(() => {
    const input = notesInput.current;
    if (!input) return;
    function resizeToContent() {
      if (!input) return;
      input.style.height = "auto";
      const styles = window.getComputedStyle(input);
      const lineHeight = Number.parseFloat(styles.lineHeight);
      const padding =
        Number.parseFloat(styles.paddingTop) + Number.parseFloat(styles.paddingBottom);
      const borders =
        Number.parseFloat(styles.borderTopWidth) + Number.parseFloat(styles.borderBottomWidth);
      const maxHeight = (Number.isFinite(lineHeight) ? lineHeight : 24) * 10 + padding + borders;
      const contentHeight = input.scrollHeight + borders;
      input.style.height = `${Math.min(contentHeight, maxHeight)}px`;
      input.style.overflowY = contentHeight > maxHeight ? "auto" : "hidden";
    }
    resizeToContent();
    let width = input.getBoundingClientRect().width;
    const observer = new ResizeObserver(() => {
      const nextWidth = input.getBoundingClientRect().width;
      if (nextWidth === width) return;
      width = nextWidth;
      resizeToContent();
    });
    observer.observe(input);
    return () => observer.disconnect();
  });

  return (
    <section className={styles["client-notes"]} aria-labelledby="client-notes-heading">
      <div className={styles["client-editor-section-heading"]}>
        <h3 id="client-notes-heading">Заметки</h3>
        <div className={styles["client-notes-actions"]}>
          <PaperTooltip title="История изменений">
            <PaperButton
              aria-label="История изменений заметок"
              aria-expanded={historyOpen}
              aria-controls={historyId}
              icon={<Icon name="history" />}
              onClick={() => setHistoryOpen((open) => !open)}
            />
          </PaperTooltip>
          {canEdit && changed ? (
            <PaperTooltip title="Сохранить заметки">
              <PaperButton
                variant="primary"
                aria-label="Сохранить"
                icon={<Icon name="check" />}
                onClick={() => onSave(value || null, initial)}
                loading={saving}
                disabled={loading || stale}
              />
            </PaperTooltip>
          ) : null}
        </div>
      </div>
      {stale ? (
        <div
          className={`${styles["client-editor-message"]} ${styles["client-editor-message-warning"]}`}
          role="status"
        >
          <p>Заметки изменились на сервере.</p>
          <PaperButton size="small" onClick={onReload}>
            Загрузить актуальные данные
          </PaperButton>
        </div>
      ) : null}
      {conflict ? (
        <p
          className={`${styles["client-editor-message"]} ${styles["client-editor-message-error"]}`}
          role="alert"
        >
          {error}
        </p>
      ) : null}
      {canEdit ? (
        <div className={styles["client-notes-input"]}>
          <textarea
            ref={notesInput}
            aria-label="Заметки клиента"
            value={value}
            rows={3}
            disabled={loading || saving}
            onChange={(event) => setValue(event.target.value)}
          />
          <span aria-live="polite">{value.length}</span>
        </div>
      ) : (
        <p className={styles["client-note-text"]}>
          {notes || <span className={styles["directory-muted"]}>Заметок пока нет</span>}
        </p>
      )}
      {loading ? <p role="status">Загрузка заметок…</p> : null}
      {historyOpen ? (
        <div id={historyId} className={styles["client-note-history"]}>
          <h4>История изменений</h4>
          {!loading && history.length === 0 ? (
            <p className={styles["directory-muted"]}>Изменений пока нет</p>
          ) : null}
          {history.length > 0 ? (
            <div className={styles["client-note-history-list"]}>
              {history.map((entry) => (
                <article className={styles["client-note-history-entry"]} key={entry.id}>
                  <div className={styles["client-note-history-meta"]}>
                    <span>{entry.authorName}</span>
                    <time dateTime={entry.changedAt}>
                      {dateTime.format(new Date(entry.changedAt))}
                    </time>
                  </div>
                  <p>
                    <span>Было: </span>
                    {entry.before || <span className={styles["directory-muted"]}>пусто</span>}
                    <br />
                    <span>Стало: </span>
                    {entry.after || (
                      <span className={styles["directory-muted"]}>заметки очищены</span>
                    )}
                  </p>
                </article>
              ))}
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
