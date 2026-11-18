import { useEffect, useState } from "react";
import { parseDateTime } from "../model/calendar";
import { DateInput, type DateInputProps } from "./DateInput";
import styles from "./date-time.module.css";
import { TimeInput } from "./TimeInput";

export type DateTimeInputProps = DateInputProps;

/** A local wall-clock value; the owner explicitly chooses a timezone before converting to UTC. */
export function DateTimeInput({
  value,
  onChange,
  id,
  "aria-label": label = "Дата и время",
  min,
  max,
  disabled,
  placeholder,
}: DateTimeInputProps) {
  const initial = parseDateTime(value);
  const [date, setDate] = useState<string | null>(initial?.date ?? null);
  const [time, setTime] = useState<string | null>(initial?.time ?? null);
  const lower = parseDateTime(min);
  const upper = parseDateTime(max);
  useEffect(() => {
    const next = parseDateTime(value);
    setDate(next?.date ?? null);
    setTime(next?.time ?? null);
  }, [value]);

  function emit(nextDate: string | null, nextTime: string | null) {
    if (!nextDate || !nextTime) {
      if (value !== null) onChange(null);
      return;
    }
    const next = `${nextDate}T${nextTime}`;
    if ((!lower || next >= (min ?? "")) && (!upper || next <= (max ?? ""))) onChange(next);
  }

  function changeDate(next: string | null) {
    setDate(next);
    let nextTime = time;
    if (next && nextTime && lower?.date === next && nextTime < lower.time) nextTime = lower.time;
    if (next && nextTime && upper?.date === next && nextTime > upper.time) nextTime = upper.time;
    setTime(nextTime);
    emit(next, nextTime);
  }

  return (
    <fieldset className={styles.dateTimeCombined} aria-label={label}>
      <DateInput
        value={date}
        onChange={changeDate}
        {...(id ? { id } : {})}
        aria-label={`${label}: дата`}
        {...(lower ? { min: lower.date } : {})}
        {...(upper ? { max: upper.date } : {})}
        {...(disabled === undefined ? {} : { disabled })}
        {...(placeholder === undefined ? {} : { placeholder })}
      />
      <TimeInput
        value={time}
        onChange={(next) => {
          setTime(next);
          emit(date, next);
        }}
        aria-label={`${label}: время`}
        {...(lower && date === lower.date ? { min: lower.time } : {})}
        {...(upper && date === upper.date ? { max: upper.time } : {})}
        {...(disabled === undefined ? {} : { disabled })}
      />
    </fieldset>
  );
}
