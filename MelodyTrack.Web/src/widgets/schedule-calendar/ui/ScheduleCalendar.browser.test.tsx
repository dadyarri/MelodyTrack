import "@/app/styles/index.css";

import dayjs from "dayjs";
import { describe, expect, it, vi } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import type { Appointment } from "@/entities/appointment";

import { AppointmentsCalendar } from "./ScheduleCalendar";

const trialAppointment: Appointment = {
  id: "01JTESTTRIAL00000000000000",
  client: { id: "01JTESTCLIENT00000000000000", firstName: "Анна", lastName: "Иванова" },
  service: { id: "01JTESTSERVICE0000000000000", name: "Пробное занятие", isTrial: true },
  provider: { id: "01JTESTUSER000000000000000", firstName: "Мария", lastName: "Петрова", roleDisplayName: "Преподаватель" },
  startDate: "2030-06-03T10:00:00Z",
  endDate: "2030-06-03T11:00:00Z",
  status: "planned",
};

describe("schedule calendar trial lessons", () => {
  it("uses the dedicated trial palette in desktop and mobile layouts", async () => {
    await page.viewport(390, 844);
    await render(
      <AppointmentsCalendar
        appointments={[trialAppointment]}
        loading={false}
        range={[dayjs("2030-06-03"), dayjs("2030-06-09")]}
        onCreateAt={vi.fn()}
        onCreateVacation={vi.fn()}
        onReschedule={vi.fn()}
        onSelect={vi.fn()}
        onComplete={vi.fn()}
        reschedulePendingAppointmentId={null}
        selectedAppointmentId={null}
      />,
    );

    const entries = [...document.querySelectorAll<HTMLElement>("[role='button']")].filter((entry) =>
      entry.textContent.includes("Пробное занятие"),
    );

    expect(entries.length).toBeGreaterThanOrEqual(2);
    for (const entry of entries) {
      expect(entry.style.getPropertyValue("--schedule-entry-border")).toBe("#7C3AED");
    }
  });
});

describe("schedule calendar actions", () => {
  it("uses a click for appointment creation when both actions are available", async () => {
    const onCreateAt = vi.fn();
    const onCreateVacation = vi.fn();
    await page.viewport(1200, 900);
    await render(
      <AppointmentsCalendar
        appointments={[]}
        canCreateVacations
        loading={false}
        range={[dayjs("2030-06-03"), dayjs("2030-06-09")]}
        visibleHours={{ startHour: 9, endHour: 12 }}
        onCreateAt={onCreateAt}
        onCreateVacation={onCreateVacation}
        onReschedule={vi.fn()}
        onSelect={vi.fn()}
        onComplete={vi.fn()}
        reschedulePendingAppointmentId={null}
        selectedAppointmentId={null}
      />,
    );

    const slot = getHourSlot("10:00");

    slot.click();

    expect(onCreateAt).toHaveBeenCalledOnce();
    expect(onCreateVacation).not.toHaveBeenCalled();
  });

  it("highlights the selected vacation range while dragging", async () => {
    const onCreateVacation = vi.fn();
    await page.viewport(1200, 900);
    await render(
      <AppointmentsCalendar
        appointments={[]}
        canCreateVacations
        loading={false}
        range={[dayjs("2030-06-03"), dayjs("2030-06-09")]}
        visibleHours={{ startHour: 9, endHour: 12 }}
        onCreateAt={vi.fn()}
        onCreateVacation={onCreateVacation}
        onReschedule={vi.fn()}
        onSelect={vi.fn()}
        onComplete={vi.fn()}
        reschedulePendingAppointmentId={null}
        selectedAppointmentId={null}
      />,
    );
    const startSlot = getHourSlot("10:00");
    const targetSlot = getHourSlot("11:00");
    const targetColumn = targetSlot.parentElement;
    const dataTransfer = new DataTransfer();

    startSlot.dispatchEvent(new DragEvent("dragstart", { bubbles: true, cancelable: true, dataTransfer }));
    await expect.poll(() => startSlot.dataset.vacationSelected).toBe("true");

    const targetBounds = targetColumn?.getBoundingClientRect();
    targetColumn?.dispatchEvent(
      new DragEvent("dragover", {
        bubbles: true,
        cancelable: true,
        clientY: (targetBounds?.top ?? 0) + 2 * 88 + 1,
        dataTransfer,
      }),
    );

    await expect.poll(() => document.querySelectorAll("[data-vacation-selected='true']").length).toBe(2);
    expect(getComputedStyle(startSlot).backgroundImage).not.toBe("none");

    targetColumn?.dispatchEvent(
      new DragEvent("drop", {
        bubbles: true,
        cancelable: true,
        clientY: (targetBounds?.top ?? 0) + 2 * 88 + 1,
        dataTransfer,
      }),
    );
    await expect.poll(() => onCreateVacation).toHaveBeenCalledOnce();
    const [startDate, endDate] = onCreateVacation.mock.calls[0] as [ReturnType<typeof dayjs>, ReturnType<typeof dayjs>];
    expect(startDate.format("YYYY-MM-DD HH:mm")).toBe("2030-06-03 10:00");
    expect(endDate.format("YYYY-MM-DD HH:mm")).toBe("2030-06-03 12:00");
  });

  it("opens an existing vacation from its unlabeled calendar band", async () => {
    const onEditVacation = vi.fn();
    await page.viewport(1200, 900);
    const screen = await render(
      <AppointmentsCalendar
        appointments={[]}
        availability={{
          userId: "01JTESTUSER000000000000000",
          workingHours: [{ dayOfWeek: "monday", isWorkingDay: true, startTime: "09:00", endTime: "13:00" }],
          vacations: [
            {
              id: "01JTESTVACATION00000000000",
              startDate: "2030-06-03T10:00:00",
              endDate: "2030-06-03T12:00:00",
            },
          ],
        }}
        loading={false}
        range={[dayjs("2030-06-03"), dayjs("2030-06-03")]}
        visibleHours={{ startHour: 9, endHour: 13 }}
        onCreateAt={vi.fn()}
        onCreateVacation={vi.fn()}
        onEditVacation={onEditVacation}
        onReschedule={vi.fn()}
        onSelect={vi.fn()}
        onComplete={vi.fn()}
        reschedulePendingAppointmentId={null}
        selectedAppointmentId={null}
      />,
    );

    await screen.getByRole("button", { name: /Изменить отпуск/ }).click();

    expect(onEditVacation).toHaveBeenCalledWith("01JTESTVACATION00000000000");
    expect(document.body.textContent).not.toContain("Отпуск · изменить");
  });
});

function getHourSlot(time: string) {
  const slot = [...document.querySelectorAll<HTMLButtonElement>("button[draggable='true']")].find((button) =>
    button.getAttribute("aria-label")?.includes(time),
  );
  if (!slot) {
    throw new Error(`Schedule slot ${time} was not rendered.`);
  }

  return slot;
}
