import { fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, describe, expect, it, vi } from "vitest";

import type { DashboardAppointment, DashboardStats } from "@/entities/dashboard";

import { DashboardContent, DashboardScheduleItem } from "./DashboardPage";

const appointment: DashboardAppointment = {
  id: "01JTESTAPPOINTMENT0000000000",
  client: {
    id: "01JTESTCLIENT00000000000000",
    firstName: "Анна",
    lastName: "Иванова",
    contacts: {
      telegram: "javascript:alert(document.domain)",
      vk: "javascript:alert(document.domain)",
    },
  },
  service: {
    id: "01JTESTSERVICE0000000000000",
    name: "Урок",
  },
  startDate: "2026-07-27T10:00:00Z",
  endDate: "2026-07-27T11:00:00Z",
  status: "planned",
};

const dashboard: DashboardStats = {
  personalClientsCount: 1,
  monthIncome: 1500,
  today: {
    date: "2026-07-29",
    count: 1,
    appointments: [appointment],
  },
  tomorrow: {
    date: "2026-07-30",
    count: 0,
    appointments: [],
  },
  organization: {
    totalClients: 8,
    debtorsCount: 2,
    totalDebt: 2400,
    totalPositiveBalance: 1500,
    appointmentsToday: 4,
    appointmentsTomorrow: 3,
    monthIncome: 18_000,
    monthExpenses: 5_000,
    monthNet: 13_000,
  },
};

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    configurable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
});

describe("DashboardContent", () => {
  it("shows only schedule cards to a regular user", () => {
    render(<DashboardContent data={dashboard} isLoading={false} isError={false} onRetry={vi.fn()} />);

    expect(screen.getByRole("heading", { name: "Обзор" })).toBeInTheDocument();
    expect(screen.getByText(/Записи на сегодня,/)).toBeInTheDocument();
    expect(screen.getByText(/Записи на завтра,/)).toBeInTheDocument();
    expect(screen.getByText("Иванова Анна — Урок")).toBeInTheDocument();
    expect(screen.queryByText("Мои клиенты")).not.toBeInTheDocument();
    expect(screen.queryByText("Мой доход за месяц")).not.toBeInTheDocument();
    expect(screen.queryByText("Записи сегодня")).not.toBeInTheDocument();
    expect(screen.queryByText("Общий долг")).not.toBeInTheDocument();
    expect(screen.queryByText("Расход за месяц")).not.toBeInTheDocument();
    expect(screen.queryByText("Клиенты с отрицательным балансом")).not.toBeInTheDocument();
  });

  it("restores all overview cards for admins and superusers", () => {
    render(<DashboardContent data={dashboard} isLoading={false} isError={false} onRetry={vi.fn()} canSeeOrganization />);

    expect(screen.getByText("Записи сегодня")).toBeInTheDocument();
    expect(screen.getByText("Записи завтра")).toBeInTheDocument();
    expect(screen.getByText("Должники")).toBeInTheDocument();
    expect(screen.getByText("Общий долг")).toBeInTheDocument();
    expect(screen.getByText("Всего клиентов")).toBeInTheDocument();
    expect(screen.getByText("Весь резерв")).toBeInTheDocument();
    expect(screen.getByText("Доход за месяц")).toBeInTheDocument();
    expect(screen.getByText("Расход за месяц")).toBeInTheDocument();
    expect(screen.getByText("Итог за месяц")).toBeInTheDocument();
    expect(screen.getByText("Клиенты с отрицательным балансом")).toBeInTheDocument();
    expect(screen.getByText("Доход за месяц").closest(".ant-card-head")).not.toBeNull();
    expect(screen.getByText(/Записи на сегодня,/).closest(".ant-card-head")).not.toBeNull();
  });

  it("keeps both schedule cards useful when there are no appointments", () => {
    render(
      <DashboardContent
        data={{
          ...dashboard,
          today: { ...dashboard.today, count: 0, appointments: [] },
        }}
        isLoading={false}
        isError={false}
        onRetry={vi.fn()}
      />,
    );

    expect(screen.getByText("На сегодня записей нет")).toBeInTheDocument();
    expect(screen.getByText("На завтра записей нет")).toBeInTheDocument();
  });

  it("offers retry without hiding the dashboard structure after an initial failure", () => {
    const onRetry = vi.fn();
    render(<DashboardContent isLoading={false} isError onRetry={onRetry} />);

    expect(screen.getByRole("heading", { name: "Обзор" })).toBeInTheDocument();
    expect(screen.getAllByText("Не удалось загрузить записи.")).toHaveLength(2);
    fireEvent.click(screen.getAllByRole("button", { name: "Повторить" })[0]);
    expect(onRetry).toHaveBeenCalledOnce();
  });
});

describe("DashboardScheduleItem", () => {
  it.each(["Telegram", "VK"])("does not render an unsafe stored %s contact", (contactTitle) => {
    render(<DashboardScheduleItem appointment={appointment} />);

    expect(screen.queryByTitle(contactTitle)).not.toBeInTheDocument();
  });

  it("renders normalized Telegram and VK contacts", () => {
    render(
      <DashboardScheduleItem
        appointment={{
          ...appointment,
          client: {
            ...appointment.client,
            contacts: {
              telegram: "@valid_name",
              vk: "https://vk.com/valid.name",
            },
          },
        }}
      />,
    );

    expect(screen.getByTitle("Telegram")).toHaveAttribute("href", "tg://resolve?domain=valid_name");
    expect(screen.getByTitle("VK")).toHaveAttribute("href", "https://vk.com/valid.name");
  });
});
