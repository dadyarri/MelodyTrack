import { fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { Calendar } from "./Calendar";
import { DateInput } from "./DateInput";
import { DateTimeInput } from "./DateTimeInput";
import { TimeInput } from "./TimeInput";

function DateTimeForm({ onChange }: { onChange: (value: string | null) => void }) {
  const [value, setValue] = useState<string | null>(null);
  return (
    <DateTimeInput
      value={value}
      onChange={(next) => {
        setValue(next);
        onChange(next);
      }}
      aria-label="Встреча"
    />
  );
}

describe("date and time inputs", () => {
  it("opens the calendar on an ordinary click of the calendar button", async () => {
    render(<DateInput value="2026-11-12" onChange={vi.fn()} aria-label="Дата" />);
    expect(screen.queryByRole("dialog", { name: "Календарь" })).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Дата: открыть календарь" }));

    const calendar = await screen.findByRole("dialog", { name: "Календарь" });
    expect(within(calendar).getByRole("button", { name: /12 ноября 2026/ })).toBeTruthy();
  });

  it("opens from the date field while keeping manual input focus and typing available", async () => {
    const onChange = vi.fn();
    render(<DateInput value="2026-11-12" onChange={onChange} aria-label="Дата" />);
    const input = screen.getByRole("textbox", { name: "Дата" });
    input.focus();

    fireEvent.click(input);

    await screen.findByRole("dialog", { name: "Календарь" });
    expect(document.activeElement).toBe(input);
    fireEvent.change(input, { target: { value: "13.11.2026" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onChange).toHaveBeenCalledWith("2026-11-13");
  });

  it("rejects an invalid or out-of-range typed date without changing the controlled value", () => {
    const onChange = vi.fn();
    render(
      <DateInput
        value={null}
        onChange={onChange}
        min="2026-11-01"
        max="2026-11-30"
        aria-label="Дата рождения"
      />,
    );
    const input = screen.getByRole("textbox", { name: "Дата рождения" });

    fireEvent.change(input, { target: { value: "31.11.2026" } });
    fireEvent.blur(input);
    expect(screen.getByRole("alert")).toBeTruthy();
    expect(onChange).not.toHaveBeenCalled();

    fireEvent.change(input, { target: { value: "01.12.2026" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onChange).not.toHaveBeenCalled();

    fireEvent.change(input, { target: { value: "12.11.2026" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onChange).toHaveBeenCalledWith("2026-11-12");
  });

  it("navigates the calendar by keyboard and never selects a disabled date", () => {
    const onSelect = vi.fn();
    const onClose = vi.fn();
    render(
      <Calendar
        value="2026-11-12"
        min="2026-11-12"
        max="2026-12-31"
        onSelect={onSelect}
        onClose={onClose}
      />,
    );
    const selected = screen.getByRole("button", { name: /12 ноября 2026/ });
    fireEvent.keyDown(selected, { key: "ArrowLeft" });
    expect(document.activeElement).toBe(selected);
    fireEvent.keyDown(selected, { key: "ArrowRight" });
    const next = screen.getByRole("button", { name: /13 ноября 2026/ });
    expect(document.activeElement).toBe(next);
    fireEvent.click(next);
    expect(onSelect).toHaveBeenCalledWith("2026-11-13");
    fireEvent.keyDown(next, { key: "Escape" });
    expect(onClose).toHaveBeenCalledOnce();
    expect(
      (screen.getByRole("button", { name: "Предыдущий месяц" }) as HTMLButtonElement).disabled,
    ).toBe(true);
  });

  it("supports week and year keyboard jumps while clamping to inclusive range edges", () => {
    const onSelect = vi.fn();
    render(
      <Calendar
        value="2026-11-12"
        min="2026-11-10"
        max="2027-12-31"
        onSelect={onSelect}
        onClose={vi.fn()}
      />,
    );
    const selected = screen.getByRole("button", { name: /12 ноября 2026/ });

    fireEvent.keyDown(selected, { key: "Home" });
    const lowerEdge = screen.getByRole("button", { name: /10 ноября 2026/ });
    expect(document.activeElement).toBe(lowerEdge);
    expect((lowerEdge as HTMLButtonElement).disabled).toBe(false);
    expect(
      (screen.getByRole("button", { name: /\D9 ноября 2026/ }) as HTMLButtonElement).disabled,
    ).toBe(true);

    fireEvent.keyDown(lowerEdge, { key: "End" });
    const sunday = screen.getByRole("button", { name: /15 ноября 2026/ });
    expect(document.activeElement).toBe(sunday);

    fireEvent.keyDown(sunday, { key: "PageDown" });
    const nextMonth = screen.getByRole("button", { name: /15 декабря 2026/ });
    expect(document.activeElement).toBe(nextMonth);
    fireEvent.keyDown(nextMonth, { key: "PageUp", shiftKey: true });
    const yearEdge = screen.getByRole("button", { name: /10 ноября 2026/ });
    expect(document.activeElement).toBe(yearEdge);
    fireEvent.keyDown(yearEdge, { key: "PageDown", shiftKey: true });
    expect(document.activeElement).toBe(screen.getByRole("button", { name: /10 ноября 2027/ }));

    fireEvent.click(screen.getByRole("button", { name: "Следующий месяц" }));
    expect(screen.getByText("декабрь 2027 г.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: /31 декабря 2027/ }));
    expect(onSelect).toHaveBeenCalledWith("2027-12-31");
  });

  it("opens from Alt+ArrowDown and keeps composed date-time limits and clearing", async () => {
    const onChange = vi.fn();
    render(
      <DateTimeInput
        value="2026-11-12T10:00"
        min="2026-11-12T09:00"
        max="2026-11-12T11:00"
        onChange={onChange}
        aria-label="Встреча"
      />,
    );
    const date = screen.getByRole("textbox", { name: "Встреча: дата" });
    fireEvent.keyDown(date, { key: "ArrowDown", altKey: true });
    const calendar = await screen.findByRole("dialog", { name: "Календарь" });
    expect(
      (within(calendar).getByRole("button", { name: /11 ноября 2026/ }) as HTMLButtonElement)
        .disabled,
    ).toBe(true);
    expect(
      (within(calendar).getByRole("button", { name: /12 ноября 2026/ }) as HTMLButtonElement)
        .disabled,
    ).toBe(false);
    expect(
      (within(calendar).getByRole("button", { name: /13 ноября 2026/ }) as HTMLButtonElement)
        .disabled,
    ).toBe(true);

    fireEvent.change(screen.getByRole("combobox", { name: "Встреча: время: часы" }), {
      target: { value: "08" },
    });
    expect(onChange).not.toHaveBeenCalledWith("2026-11-12T08:00");
    fireEvent.change(screen.getByRole("combobox", { name: "Встреча: время: часы" }), {
      target: { value: "11" },
    });
    fireEvent.change(screen.getByRole("combobox", { name: "Встреча: время: минуты" }), {
      target: { value: "00" },
    });
    expect(onChange).toHaveBeenLastCalledWith("2026-11-12T11:00");

    fireEvent.click(screen.getByRole("button", { name: "Встреча: дата: очистить" }));
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it("selects a 24-hour time and clears it without using native date or time inputs", () => {
    const onChange = vi.fn();
    const view = render(<TimeInput value={null} onChange={onChange} aria-label="Начало" />);
    fireEvent.change(screen.getByRole("combobox", { name: "Начало: часы" }), {
      target: { value: "09" },
    });
    expect(onChange).not.toHaveBeenCalled();
    fireEvent.change(screen.getByRole("combobox", { name: "Начало: минуты" }), {
      target: { value: "30" },
    });
    expect(onChange).toHaveBeenCalledWith("09:30");

    view.rerender(<TimeInput value="09:30" onChange={onChange} aria-label="Начало" />);
    fireEvent.click(screen.getByRole("button", { name: "Начало: очистить" }));
    expect(onChange).toHaveBeenLastCalledWith(null);
    expect(
      view.container.querySelector(
        'input[type="time"], input[type="date"], input[type="datetime-local"]',
      ),
    ).toBeNull();
  });

  it("combines a date and time as a local value without introducing a timezone", () => {
    const onChange = vi.fn();
    render(<DateTimeForm onChange={onChange} />);
    const group = screen.getByRole("group", { name: "Встреча" });
    const date = within(group).getByRole("textbox", { name: "Встреча: дата" });
    fireEvent.change(date, { target: { value: "12.11.2026" } });
    fireEvent.blur(date);
    fireEvent.change(screen.getByRole("combobox", { name: "Встреча: время: часы" }), {
      target: { value: "09" },
    });
    fireEvent.change(screen.getByRole("combobox", { name: "Встреча: время: минуты" }), {
      target: { value: "30" },
    });

    expect(onChange).toHaveBeenLastCalledWith("2026-11-12T09:30");
  });
});
