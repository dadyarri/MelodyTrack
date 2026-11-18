import { useEffect, useId, useState } from "react";
import { Icon } from "../../Icon";
import { parseTime } from "../model/calendar";
import styles from "./date-time.module.css";

export type TimeInputProps = {
  value: string | null;
  onChange: (value: string | null) => void;
  id?: string;
  "aria-label"?: string;
  min?: string;
  max?: string;
  disabled?: boolean;
};

const hours = Array.from({ length: 24 }, (_, index) => String(index).padStart(2, "0"));
const minutes = Array.from({ length: 60 }, (_, index) => String(index).padStart(2, "0"));

export function TimeInput({
  value,
  onChange,
  id,
  "aria-label": label = "Время",
  min,
  max,
  disabled = false,
}: TimeInputProps) {
  const generated = useId();
  const inputId = id ?? generated;
  const parsed = parseTime(value);
  const [hour, setHour] = useState(parsed?.hour ?? "");
  const [minute, setMinute] = useState(parsed?.minute ?? "");
  useEffect(() => {
    const next = parseTime(value);
    setHour(next?.hour ?? "");
    setMinute(next?.minute ?? "");
  }, [value]);

  function allowed(time: string) {
    return (!parseTime(min) || time >= (min ?? "")) && (!parseTime(max) || time <= (max ?? ""));
  }

  function changeHour(next: string) {
    setHour(next);
    if (!minute) return;
    const nextMinute = allowed(`${next}:${minute}`)
      ? minute
      : minutes.find((entry) => allowed(`${next}:${entry}`));
    if (nextMinute === undefined) return;
    setMinute(nextMinute);
    onChange(`${next}:${nextMinute}`);
  }

  return (
    <fieldset
      className={`${styles.dateTimeClock}${disabled ? ` ${styles.dateTimeInputDisabled}` : ""}`}
      aria-label={label}
    >
      <select
        id={inputId}
        value={hour}
        aria-label={`${label}: часы`}
        disabled={disabled}
        onChange={(event) => changeHour(event.target.value)}
      >
        <option value="" disabled>
          ЧЧ
        </option>
        {hours.map((entry) => (
          <option
            key={entry}
            value={entry}
            disabled={!minutes.some((part) => allowed(`${entry}:${part}`))}
          >
            {entry}
          </option>
        ))}
      </select>
      <span aria-hidden="true">:</span>
      <select
        value={minute}
        aria-label={`${label}: минуты`}
        disabled={disabled}
        onChange={(event) => {
          const next = event.target.value;
          setMinute(next);
          if (hour && allowed(`${hour}:${next}`)) onChange(`${hour}:${next}`);
        }}
      >
        <option value="" disabled>
          ММ
        </option>
        {minutes.map((entry) => (
          <option key={entry} value={entry} disabled={hour ? !allowed(`${hour}:${entry}`) : false}>
            {entry}
          </option>
        ))}
      </select>
      {(hour || minute) && (
        <button
          type="button"
          className={styles.dateTimeClear}
          aria-label={`${label}: очистить`}
          disabled={disabled}
          onClick={() => {
            setHour("");
            setMinute("");
            onChange(null);
          }}
        >
          <Icon name="close" size={18} />
        </button>
      )}
    </fieldset>
  );
}
