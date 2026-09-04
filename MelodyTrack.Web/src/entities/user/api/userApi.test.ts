import { beforeEach, describe, expect, it, vi } from "vitest";

const httpMock = vi.hoisted(() => ({
  get: vi.fn(),
}));

vi.mock("@/shared/api", () => ({
  http: httpMock,
}));

import { usersApi } from "./userApi";

describe("usersApi", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("requests the planned appointment conflict count for the selected vacation range", async () => {
    const signal = new AbortController().signal;
    httpMock.get.mockResolvedValue({ data: 2 });

    const count = await usersApi.getVacationAppointmentConflictCount(
      "01JTESTUSER0000000000000000",
      "2026-08-12T09:00:00.000Z",
      "2026-08-12T12:00:00.000Z",
      signal,
    );

    expect(httpMock.get).toHaveBeenCalledWith("/users/01JTESTUSER0000000000000000/vacation-appointment-conflict-count", {
      params: {
        startDate: "2026-08-12T09:00:00.000Z",
        endDate: "2026-08-12T12:00:00.000Z",
      },
      signal,
    });
    expect(count).toBe(2);
  });
});
