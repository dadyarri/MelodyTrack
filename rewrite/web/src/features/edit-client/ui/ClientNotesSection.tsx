import { Alert, Button, Input, Space, Tooltip } from "antd";
import { useEffect, useId, useRef, useState } from "react";
import { Icon } from "@/shared/ui/Icon";
import { useErrorNotification } from "@/shared/ui/notifications";

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

  return (
    <section className="client-notes" aria-labelledby="client-notes-heading">
      <div className="client-editor-section-heading">
        <h3 id="client-notes-heading">Заметки</h3>
        <div className="client-notes-actions">
          <Tooltip title="История изменений">
            <Button
              aria-label="История изменений заметок"
              aria-expanded={historyOpen}
              aria-controls={historyId}
              icon={<Icon name="history" />}
              onClick={() => setHistoryOpen((open) => !open)}
            />
          </Tooltip>
          {canEdit && changed ? (
            <Tooltip title="Сохранить заметки">
              <Button
                type="primary"
                aria-label="Сохранить"
                icon={<Icon name="check" />}
                onClick={() => onSave(value || null, initial)}
                loading={saving}
                disabled={loading || stale}
              />
            </Tooltip>
          ) : null}
        </div>
      </div>
      {stale ? (
        <Alert
          type="warning"
          showIcon
          title="Заметки изменились на сервере."
          action={
            <Button size="small" onClick={onReload}>
              Загрузить актуальные данные
            </Button>
          }
        />
      ) : null}
      {conflict ? <Alert type="error" showIcon title={error} /> : null}
      {canEdit ? (
        <Input.TextArea
          aria-label="Заметки клиента"
          value={value}
          autoSize={{ minRows: 3, maxRows: 10 }}
          showCount
          disabled={loading || saving}
          onChange={(event) => setValue(event.target.value)}
        />
      ) : (
        <p className="client-note-text">
          {notes || <span className="directory-muted">Заметок пока нет</span>}
        </p>
      )}
      {loading ? <p role="status">Загрузка заметок…</p> : null}
      {historyOpen ? (
        <div id={historyId} className="client-note-history">
          <h4>История изменений</h4>
          {!loading && history.length === 0 ? (
            <p className="directory-muted">Изменений пока нет</p>
          ) : null}
          {history.length > 0 ? (
            <Space orientation="vertical" size={12} className="wide">
              {history.map((entry) => (
                <article className="client-note-history-entry" key={entry.id}>
                  <div className="client-note-history-meta">
                    <span>{entry.authorName}</span>
                    <time dateTime={entry.changedAt}>
                      {dateTime.format(new Date(entry.changedAt))}
                    </time>
                  </div>
                  <p>
                    <span className="client-note-history-label">Было: </span>
                    {entry.before || <span className="directory-muted">пусто</span>}
                    <br />
                    <span className="client-note-history-label">Стало: </span>
                    {entry.after || <span className="directory-muted">заметки очищены</span>}
                  </p>
                </article>
              ))}
            </Space>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
