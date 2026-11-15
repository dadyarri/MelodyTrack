import { Popover } from "antd";
import { type KeyboardEvent, useEffect, useId, useRef, useState } from "react";
import { Icon } from "../../Icon";
import { displayDate, parseDateInput, withinDateRange } from "../model/calendar";
import { Calendar } from "./Calendar";
import "./date-time.css";

function dateInputError(draft: string, min?: string, max?: string): string {
  if (!draft.trim()) return "";
  const parsed = parseDateInput(draft);
  return parsed && withinDateRange(parsed, min, max)
    ? ""
    : "Введите допустимую дату в формате ДД.ММ.ГГГГ.";
}

export type DateInputProps = {
  value: string | null;
  onChange: (value: string | null) => void;
  id?: string;
  "aria-label"?: string;
  min?: string;
  max?: string;
  disabled?: boolean;
  placeholder?: string;
};

export function DateInput({
  value,
  onChange,
  id,
  "aria-label": label = "Дата",
  min,
  max,
  disabled = false,
  placeholder = "ДД.ММ.ГГГГ",
}: DateInputProps) {
  const generated = useId();
  const inputId = id ?? generated;
  const [draft, setDraft] = useState(() => displayDate(value));
  const [invalid, setInvalid] = useState(false);
  const [open, setOpen] = useState(false);
  const [calendarAutoFocus, setCalendarAutoFocus] = useState(true);
  const input = useRef<HTMLInputElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    setDraft(displayDate(value));
    setInvalid(false);
    input.current?.setCustomValidity("");
  }, [value]);

  useEffect(() => {
    input.current?.setCustomValidity(dateInputError(draft, min, max));
  }, [draft, min, max]);

  function commit() {
    if (!draft.trim()) {
      setInvalid(false);
      onChange(null);
      return;
    }
    const parsed = parseDateInput(draft);
    if (!parsed || !withinDateRange(parsed, min, max)) {
      setInvalid(true);
      return;
    }
    setDraft(displayDate(parsed));
    setInvalid(false);
    if (parsed !== value) onChange(parsed);
  }

  function keyboard(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter") {
      event.preventDefault();
      commit();
    }
    if (event.key === "ArrowDown" && event.altKey) {
      event.preventDefault();
      setCalendarAutoFocus(true);
      setOpen(true);
    }
    if (event.key === "Escape") {
      setDraft(displayDate(value));
      setInvalid(false);
      setOpen(false);
    }
  }

  return (
    <div className="date-time-field">
      <Popover
        trigger="click"
        placement="bottomLeft"
        open={open && !disabled}
        onOpenChange={(next) => {
          if (next) setCalendarAutoFocus(true);
          setOpen(next);
        }}
        destroyOnHidden
        fresh
        content={
          <Calendar
            value={value}
            min={min}
            max={max}
            autoFocus={open && calendarAutoFocus}
            onSelect={(selected) => {
              setDraft(displayDate(selected));
              setInvalid(false);
              input.current?.setCustomValidity("");
              onChange(selected);
              setOpen(false);
              input.current?.focus();
            }}
            onClose={() => {
              setOpen(false);
              trigger.current?.focus();
            }}
          />
        }
      >
        <div
          className={`date-time-input${invalid ? " date-time-input-invalid" : ""}${disabled ? " date-time-input-disabled" : ""}`}
        >
          <input
            ref={input}
            id={inputId}
            type="text"
            inputMode="numeric"
            autoComplete="off"
            value={draft}
            disabled={disabled}
            placeholder={placeholder}
            aria-label={label}
            aria-invalid={invalid || undefined}
            aria-describedby={invalid ? `${inputId}-error` : undefined}
            onInvalid={() => setInvalid(true)}
            onChange={(event) => {
              event.target.setCustomValidity(dateInputError(event.target.value, min, max));
              setDraft(event.target.value);
              setInvalid(false);
            }}
            onBlur={commit}
            onKeyDown={keyboard}
            onClick={(event) => {
              event.stopPropagation();
              setCalendarAutoFocus(false);
              setOpen(true);
            }}
          />
          {(draft || value) && (
            <button
              type="button"
              className="date-time-clear"
              aria-label={`${label}: очистить`}
              disabled={disabled}
              onClick={(event) => {
                event.stopPropagation();
                setDraft("");
                setInvalid(false);
                input.current?.setCustomValidity("");
                setOpen(false);
                onChange(null);
                input.current?.focus();
              }}
            >
              <Icon name="close" size={18} />
            </button>
          )}
          <button
            ref={trigger}
            type="button"
            className="date-time-calendar-trigger"
            aria-label={`${label}: открыть календарь`}
            aria-haspopup="dialog"
            aria-expanded={open && !disabled}
            disabled={disabled}
          >
            <Icon name="calendar" size={18} />
          </button>
        </div>
      </Popover>
      {invalid && (
        <span id={`${inputId}-error`} className="date-time-error" role="alert">
          Введите допустимую дату в формате ДД.ММ.ГГГГ.
        </span>
      )}
    </div>
  );
}
