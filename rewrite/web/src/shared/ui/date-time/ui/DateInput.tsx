import { Popover } from "@base-ui/react/popover";
import { type KeyboardEvent, useEffect, useId, useRef, useState } from "react";
import { Icon } from "../../Icon";
import { displayDate, parseDateInput, withinDateRange } from "../model/calendar";
import { Calendar } from "./Calendar";
import styles from "./date-time.module.css";

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
  const anchor = useRef<HTMLDivElement>(null);
  const calendarPopup = useRef<HTMLDivElement>(null);
  const returnToInput = useRef(false);

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
      returnToInput.current = true;
      setCalendarAutoFocus(true);
      setOpen(true);
    }
    if (event.key === "Escape") {
      if (open) {
        event.preventDefault();
        event.stopPropagation();
      }
      setDraft(displayDate(value));
      setInvalid(false);
      setOpen(false);
    }
  }

  return (
    <div className={styles.dateTimeField}>
      <Popover.Root
        open={open && !disabled}
        onOpenChange={(next) => {
          if (next) {
            setCalendarAutoFocus(true);
            returnToInput.current = false;
          }
          setOpen(next);
        }}
      >
        <div
          ref={anchor}
          className={`${styles.dateTimeInput}${invalid ? ` ${styles.dateTimeInputInvalid}` : ""}${disabled ? ` ${styles.dateTimeInputDisabled}` : ""}`}
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
              returnToInput.current = true;
              setCalendarAutoFocus(false);
              setOpen(true);
            }}
          />
          {(draft || value) && (
            <button
              type="button"
              className={styles.dateTimeClear}
              aria-label={`${label}: очистить`}
              disabled={disabled}
              onClick={(event) => {
                event.stopPropagation();
                setDraft("");
                setInvalid(false);
                input.current?.setCustomValidity("");
                returnToInput.current = true;
                setOpen(false);
                onChange(null);
                input.current?.focus();
              }}
            >
              <Icon name="close" size={18} />
            </button>
          )}
          <Popover.Trigger
            ref={trigger}
            type="button"
            aria-label={`${label}: открыть календарь`}
            aria-haspopup="dialog"
            aria-expanded={open && !disabled}
            disabled={disabled}
          >
            <Icon name="calendar" size={18} />
          </Popover.Trigger>
        </div>
        <Popover.Portal>
          <Popover.Positioner
            anchor={anchor}
            align="start"
            sideOffset={6}
            className={styles.dateTimePositioner}
          >
            <Popover.Popup
              ref={calendarPopup}
              className={styles.dateTimePopover}
              role="presentation"
              data-popup-scroll-boundary=""
              initialFocus={() =>
                calendarAutoFocus
                  ? (calendarPopup.current?.querySelector<HTMLButtonElement>(
                      `.${styles.dateTimeCalendarDay}[tabindex='0']`,
                    ) ?? false)
                  : false
              }
              finalFocus={() => (returnToInput.current ? input.current : trigger.current)}
            >
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
                  returnToInput.current = true;
                  setOpen(false);
                  input.current?.focus();
                }}
                onClose={() => {
                  returnToInput.current = false;
                  setOpen(false);
                  trigger.current?.focus();
                }}
              />
            </Popover.Popup>
          </Popover.Positioner>
        </Popover.Portal>
      </Popover.Root>
      {invalid && (
        <span id={`${inputId}-error`} className={styles.dateTimeError} role="alert">
          Введите допустимую дату в формате ДД.ММ.ГГГГ.
        </span>
      )}
    </div>
  );
}
