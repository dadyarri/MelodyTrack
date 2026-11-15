import { type KeyboardEvent, useEffect, useRef, useState } from "react";
import { Icon } from "../../Icon";
import {
  addDays,
  addMonths,
  type CalendarDate,
  clampDate,
  dateAsUtc,
  dateValue,
  daysInMonth,
  parseDate,
  todayDate,
  withinDateRange,
} from "../model/calendar";

const weekdays = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"];
const monthFormat = new Intl.DateTimeFormat("ru-RU", {
  month: "long",
  year: "numeric",
  timeZone: "UTC",
});
const dayFormat = new Intl.DateTimeFormat("ru-RU", { dateStyle: "full", timeZone: "UTC" });

export function Calendar({
  value,
  min,
  max,
  onSelect,
  onClose,
  autoFocus = true,
}: {
  value: string | null;
  min: string | undefined;
  max: string | undefined;
  onSelect: (value: string) => void;
  onClose: () => void;
  autoFocus?: boolean;
}) {
  const [focused, setFocused] = useState(() =>
    clampDate(parseDate(value) ?? todayDate(), min, max),
  );
  const [month, setMonth] = useState(() => ({ ...focused, day: 1 }));
  const cells = useRef(new Map<string, HTMLButtonElement>());
  const shouldFocus = useRef(autoFocus);
  const today = dateValue(todayDate());
  const offset = (dateAsUtc(month).getUTCDay() + 6) % 7;
  const dates = Array.from({ length: 42 }, (_, index) => {
    const date = addDays(month, index - offset);
    return { date, key: date ? dateValue(date) : `boundary-${index}` };
  });

  useEffect(() => {
    shouldFocus.current = autoFocus;
    if (autoFocus) [...cells.current.values()].find((cell) => cell.tabIndex === 0)?.focus();
  }, [autoFocus]);

  useEffect(() => {
    if (shouldFocus.current) cells.current.get(dateValue(focused))?.focus();
  }, [focused]);

  function focusDate(target: CalendarDate | null) {
    if (!target) return;
    const next = clampDate(target, min, max);
    shouldFocus.current = true;
    setMonth({ ...next, day: 1 });
    setFocused(next);
  }

  function keyboard(event: KeyboardEvent<HTMLButtonElement>, date: CalendarDate) {
    const weekday = (dateAsUtc(date).getUTCDay() + 6) % 7;
    const shifts: Record<string, number> = {
      ArrowLeft: -1,
      ArrowRight: 1,
      ArrowUp: -7,
      ArrowDown: 7,
      Home: -weekday,
      End: 6 - weekday,
    };
    if (event.key === "Escape") {
      event.preventDefault();
      onClose();
    } else if (event.key === "PageUp" || event.key === "PageDown") {
      event.preventDefault();
      focusDate(addMonths(date, (event.key === "PageUp" ? -1 : 1) * (event.shiftKey ? 12 : 1)));
    } else if (event.key in shifts) {
      event.preventDefault();
      focusDate(addDays(date, shifts[event.key] ?? 0));
    }
  }

  function canNavigate(count: number) {
    const target = addMonths(month, count);
    return (
      target !== null &&
      (!parseDate(max) || dateValue(target) <= (max ?? "")) &&
      (!parseDate(min) ||
        dateValue({ ...target, day: daysInMonth(target.year, target.month) }) >= (min ?? ""))
    );
  }

  function navigate(count: number) {
    const target = addMonths(month, count);
    if (!target || !canNavigate(count)) return;
    shouldFocus.current = false;
    setMonth(target);
    setFocused(
      clampDate(
        { ...target, day: Math.min(focused.day, daysInMonth(target.year, target.month)) },
        min,
        max,
      ),
    );
  }

  function closeOnEscape(event: KeyboardEvent<HTMLButtonElement>) {
    if (event.key === "Escape") {
      event.preventDefault();
      onClose();
    }
  }

  return (
    <dialog open className="date-time-calendar" aria-label="Календарь">
      <div className="date-time-calendar-heading">
        <button
          type="button"
          aria-label="Предыдущий год"
          disabled={!canNavigate(-12)}
          onKeyDown={closeOnEscape}
          onClick={() => navigate(-12)}
        >
          «
        </button>
        <button
          type="button"
          aria-label="Предыдущий месяц"
          disabled={!canNavigate(-1)}
          onKeyDown={closeOnEscape}
          onClick={() => navigate(-1)}
        >
          <Icon name="previous" size={18} />
        </button>
        <span className="date-time-calendar-month" aria-live="polite">
          {monthFormat.format(dateAsUtc(month))}
        </span>
        <button
          type="button"
          aria-label="Следующий месяц"
          disabled={!canNavigate(1)}
          onKeyDown={closeOnEscape}
          onClick={() => navigate(1)}
        >
          <Icon name="next" size={18} />
        </button>
        <button
          type="button"
          aria-label="Следующий год"
          disabled={!canNavigate(12)}
          onKeyDown={closeOnEscape}
          onClick={() => navigate(12)}
        >
          »
        </button>
      </div>
      <table className="date-time-calendar-grid" aria-label="Выбор даты">
        <thead>
          <tr>
            {weekdays.map((day) => (
              <th scope="col" key={day}>
                {day}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {[0, 1, 2, 3, 4, 5].map((week) => (
            <tr key={week}>
              {dates.slice(week * 7, week * 7 + 7).map(({ date, key }) => {
                if (!date) return <td key={key} />;
                const iso = dateValue(date);
                const disabled = !withinDateRange(iso, min, max);
                return (
                  <td key={iso}>
                    <button
                      ref={(node) => {
                        if (node) cells.current.set(iso, node);
                        else cells.current.delete(iso);
                      }}
                      type="button"
                      className={`date-time-calendar-day${date.month !== month.month ? " date-time-calendar-outside" : ""}${iso === value ? " date-time-calendar-selected" : ""}`}
                      aria-label={dayFormat.format(dateAsUtc(date))}
                      aria-current={iso === today ? "date" : undefined}
                      aria-pressed={iso === value}
                      tabIndex={iso === dateValue(focused) ? 0 : -1}
                      disabled={disabled}
                      onFocus={() => {
                        shouldFocus.current = false;
                        setFocused(date);
                      }}
                      onKeyDown={(event) => keyboard(event, date)}
                      onClick={() => onSelect(iso)}
                    >
                      {date.day}
                    </button>
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
      <button
        className="date-time-today"
        type="button"
        disabled={!withinDateRange(today, min, max)}
        onKeyDown={closeOnEscape}
        onClick={() => onSelect(today)}
      >
        Сегодня
      </button>
    </dialog>
  );
}
