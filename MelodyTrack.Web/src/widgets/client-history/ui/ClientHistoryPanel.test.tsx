import { render, screen } from "@testing-library/react";
import { beforeAll, describe, expect, it, vi } from "vitest";

import type { ClientHistory } from "@/entities/client";

import { ClientHistoryPanel } from "./ClientHistoryPanel";

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

describe("ClientHistoryPanel contacts", () => {
  it.each(["Telegram", "VK"])("does not render an unsafe stored %s contact", () => {
    render(
      <ClientHistoryPanel
        data={createHistory({ telegram: "javascript:alert(document.domain)", vk: "javascript:alert(document.domain)" })}
      />,
    );

    expect(document.querySelector('a[href^="javascript:"]')).not.toBeInTheDocument();
  });

  it("renders normalized Telegram and VK contacts", () => {
    render(<ClientHistoryPanel data={createHistory({ telegram: "@valid_name", vk: "https://vk.com/valid.name" })} />);

    expect(screen.getByRole("link", { name: "@valid_name" })).toHaveAttribute("href", "tg://resolve?domain=valid_name");
    expect(screen.getByRole("link", { name: "@valid.name" })).toHaveAttribute("href", "https://vk.com/valid.name");
  });
});

function createHistory(contacts: { telegram: string; vk: string }): ClientHistory {
  return {
    client: {
      id: "client-1",
      firstName: "Анна",
      lastName: "Клиент",
      createdAtUtc: "2026-07-01T00:00:00Z",
      isLeadClosed: false,
      vacations: [],
      balance: 0,
      lifecycleStatus: 1,
      contacts,
    },
    summary: {
      totalPayments: 0,
      paymentsCount: 0,
      completedAppointmentsCount: 0,
      upcomingAppointmentsCount: 0,
    },
    events: {
      items: [],
      page: {
        page: 1,
        pageSize: 20,
        total: 0,
        hasNextPage: false,
        hasPrevPage: false,
      },
    },
  };
}
