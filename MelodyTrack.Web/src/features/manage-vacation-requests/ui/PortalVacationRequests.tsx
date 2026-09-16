import { DatePicker } from "antd";
import dayjs, { type Dayjs } from "dayjs";
import { type SyntheticEvent, useState } from "react";

import { usePortalAppearance } from "@/shared/config";
import { RpgApiError, RpgBadge, RpgButton, RpgPanel, RpgState } from "@/shared/ui/rpg";

import type { VacationRequest, VacationRequestStatus } from "../model/types";
import styles from "./PortalVacationRequests.module.css";

const labels: Record<VacationRequestStatus, string> = {
  pending: "Ожидает решения",
  approved: "Одобрена",
  declined: "Отклонена",
  cancelled: "Отозвана",
};

export function PortalVacationRequests({
  requests,
  loading,
  error,
  creating,
  cancelling,
  cancelled,
  mutationError,
  onRetry,
  onCreate,
  onCancel,
}: {
  requests: VacationRequest[];
  loading: boolean;
  error: unknown;
  mutationError: unknown;
  creating: boolean;
  cancelling: boolean;
  cancelled: boolean;
  onRetry: () => void;
  onCreate: (values: { period: [Dayjs, Dayjs]; message?: string }) => Promise<unknown>;
  onCancel: (request: VacationRequest) => void;
}) {
  const [period, setPeriod] = useState<[Dayjs, Dayjs] | null>(null);
  const [comment, setComment] = useState("");
  const [validation, setValidation] = useState("");
  const [sent, setSent] = useState(false);
  const { appearance } = usePortalAppearance();
  const pickerClassName = appearance === "minimal" ? styles.minimalPicker : styles.rpgPicker;
  const pickerPopupClassName = appearance === "minimal" ? styles.minimalPickerPopup : styles.rpgPickerPopup;

  const submit = async (event: SyntheticEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (creating) return;
    if (!period || !period[0].isValid() || !period[1].isValid() || !period[1].isAfter(period[0])) {
      setValidation("Окончание должно быть позже начала отпуска.");
      return;
    }
    setValidation("");
    setSent(false);
    try {
      await onCreate({ period, message: comment });
      setPeriod(null);
      setComment("");
      setSent(true);
    } catch {
      /* The mutation error remains visible; keep the entered values. */
    }
  };

  return (
    <div className={styles.page}>
      <h1>Отпуска</h1>
      <p>Сообщите, когда не сможете заниматься. Расписание изменится после одобрения заявки.</p>
      <div className={styles.columns}>
        <RpgPanel className={styles.panel}>
          <h2>Новая заявка</h2>
          <form
            onSubmit={(event) => {
              void submit(event);
            }}
            className={styles.form}
          >
            <fieldset disabled={creating} className={styles.fields}>
              <div className={styles.fieldLabel}>
                Период отпуска
                <DatePicker.RangePicker
                  className={`${styles.periodPicker} ${pickerClassName}`}
                  aria-label="Период отпуска"
                  value={period}
                  format="DD.MM.YYYY HH:mm"
                  showTime={{ format: "HH:mm", minuteStep: 15 }}
                  popupClassName={pickerPopupClassName}
                  getPopupContainer={(trigger) => trigger.parentElement ?? document.body}
                  onChange={(value) => {
                    setPeriod(value?.[0] && value[1] ? [value[0], value[1]] : null);
                  }}
                />
              </div>
              <label>
                Комментарий <span className={styles.muted}>(необязательно)</span>
                <textarea
                  rows={3}
                  maxLength={500}
                  value={comment}
                  onChange={(event) => {
                    setComment(event.target.value);
                  }}
                />
              </label>
            </fieldset>
            {validation ? <p role="alert">{validation}</p> : null}
            <div>
              <RpgButton type="submit" variant="primary" pending={creating} pendingLabel="Отправляем…">
                Отправить заявку
              </RpgButton>
            </div>
          </form>
          {sent ? <RpgState kind="success" title="Заявка отправлена" /> : null}
          {mutationError ? <RpgApiError error={mutationError} /> : null}
        </RpgPanel>
        <section className={styles.history} aria-labelledby="vacation-history">
          <h2 id="vacation-history">Мои заявки</h2>
          {cancelled ? <RpgState kind="success" title="Заявка отозвана" /> : null}
          {loading ? <RpgState kind="loading" title="Загружаем заявки" /> : null}
          {error ? <RpgApiError error={error} onRetry={onRetry} /> : null}
          {!loading && !error && !requests.length ? <RpgState kind="empty" title="Заявок пока нет" /> : null}
          {requests.map((request) => (
            <RpgPanel key={request.id} className={styles.panel}>
              <div>
                <RpgBadge tone={request.status === "approved" ? "success" : request.status === "pending" ? "warning" : "neutral"}>
                  {labels[request.status]}
                </RpgBadge>
              </div>
              <h3>
                {dayjs(request.startDate).format("D MMM YYYY, HH:mm")} — {dayjs(request.endDate).format("D MMM YYYY, HH:mm")}
              </h3>
              {request.requestMessage ? <p className={styles.message}>{request.requestMessage}</p> : null}
              {request.decisionMessage ? <p className={styles.message}>Ответ: {request.decisionMessage}</p> : null}
              <p className={styles.muted}>Создана {dayjs(request.createdAtUtc).format("D MMM YYYY, HH:mm")}</p>
              {request.status === "pending" ? (
                <div>
                  <RpgButton
                    variant="quiet"
                    disabled={cancelling}
                    onClick={() => {
                      onCancel(request);
                    }}
                  >
                    Отозвать заявку
                  </RpgButton>
                </div>
              ) : null}
            </RpgPanel>
          ))}
        </section>
      </div>
    </div>
  );
}
