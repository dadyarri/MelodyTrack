import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import dayjs from "dayjs";
import type { ReactNode } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

import { usersApi } from "@/entities/user";

import { VacationRangeModal } from "./VacationRangeModal";

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

describe("VacationRangeModal", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("does not offer appointment cancellation when the selected range has no conflicts", async () => {
    const getConflictCount = vi.spyOn(usersApi, "getVacationAppointmentConflictCount").mockResolvedValue(0);

    renderModal();

    await waitFor(() => {
      expect(getConflictCount).toHaveBeenCalled();
    });
    expect(screen.queryByText(/Отменить пересекающиеся запланированные занятия/)).not.toBeInTheDocument();
  });

  it("offers appointment cancellation when the selected range has conflicts", async () => {
    vi.spyOn(usersApi, "getVacationAppointmentConflictCount").mockResolvedValue(2);

    renderModal();

    expect(await screen.findByText("Пересекающихся запланированных занятий: 2.")).toBeInTheDocument();
    expect(screen.getByText(/Отменить пересекающиеся запланированные занятия/)).toBeInTheDocument();
  });
});

function renderModal() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;

  return render(
    <VacationRangeModal
      initialPeriod={[dayjs("2026-08-12T09:00:00Z"), dayjs("2026-08-12T12:00:00Z")]}
      open
      pending={false}
      requestApproval={false}
      subjectId="01JTESTUSER0000000000000000"
      onCancel={vi.fn()}
      onSubmit={vi.fn()}
    />,
    { wrapper },
  );
}
