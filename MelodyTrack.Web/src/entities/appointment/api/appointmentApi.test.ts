import { beforeEach, describe, expect, it, vi } from "vitest";

const httpMock = vi.hoisted(() => ({
  delete: vi.fn(),
  get: vi.fn(),
  patch: vi.fn(),
  post: vi.fn(),
}));

vi.mock("@/shared/api", () => ({
  http: httpMock,
}));

import { appointmentsApi } from "./appointmentApi";
import { appointmentQueryKeys } from "./queryKeys";

describe("appointmentsApi", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("passes the OpenAPI schedule window parameters", async () => {
    httpMock.get.mockResolvedValue({ data: { appointments: [] } });
    const params = {
      timezone: "Europe/Moscow",
      startDate: "2026-07-20T00:00:00.000Z",
      endDate: "2026-07-27T00:00:00.000Z",
    };

    await expect(appointmentsApi.list(params)).resolves.toEqual([]);
    expect(httpMock.get).toHaveBeenCalledWith("/appointments", { params });
  });

  it("preserves the replay key when creating an appointment", async () => {
    httpMock.post.mockResolvedValue({ data: { id: "appointment-1" } });
    const input = {
      clientId: "client-1",
      serviceId: "service-1",
      startDate: "2026-07-25T12:00:00.000Z",
      timezone: "Europe/Moscow",
    };

    await appointmentsApi.create(input, { idempotencyKey: "replay-1" });

    expect(httpMock.post).toHaveBeenCalledWith("/appointments", input, {
      headers: {
        "Idempotency-Key": "replay-1",
      },
    });
  });

  it("sends delete scope and concurrency data in the OpenAPI request body", async () => {
    httpMock.delete.mockResolvedValue({ data: undefined });

    await appointmentsApi.remove("appointment-1", {
      scope: "this-and-following",
      expectedActivityId: "activity-1",
    });

    expect(httpMock.delete).toHaveBeenCalledWith("/appointments/appointment-1", {
      data: {
        scope: "this-and-following",
        expectedActivityId: "activity-1",
      },
    });
  });

  it("preserves the existing query-key shape", () => {
    expect(appointmentQueryKeys.all).toEqual(["schedule"]);
    expect(appointmentQueryKeys.appointmentsAll).toEqual(["schedule", "appointments"]);
    expect(appointmentQueryKeys.appointments("start", "end")).toEqual(["schedule", "appointments", "start", "end"]);
  });
});
