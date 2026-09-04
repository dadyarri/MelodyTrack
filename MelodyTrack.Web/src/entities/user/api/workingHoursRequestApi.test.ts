import { beforeEach, describe, expect, it, vi } from "vitest";

const httpMock = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
}));

vi.mock("@/shared/api", () => ({
  http: httpMock,
}));

import { workingHoursRequestsApi } from "./workingHoursRequestApi";

describe("workingHoursRequestsApi", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("submits the complete working-hours snapshot without a subject identifier", async () => {
    const input = {
      workingHours: [
        { dayOfWeek: "monday" as const, isWorkingDay: true, startTime: "09:00", endTime: "18:00" },
        { dayOfWeek: "tuesday" as const, isWorkingDay: true, startTime: "09:00", endTime: "18:00" },
        { dayOfWeek: "wednesday" as const, isWorkingDay: true, startTime: "09:00", endTime: "18:00" },
        { dayOfWeek: "thursday" as const, isWorkingDay: true, startTime: "09:00", endTime: "18:00" },
        { dayOfWeek: "friday" as const, isWorkingDay: true, startTime: "09:00", endTime: "18:00" },
        { dayOfWeek: "saturday" as const, isWorkingDay: false },
        { dayOfWeek: "sunday" as const, isWorkingDay: false },
      ],
    };
    httpMock.post.mockResolvedValue({ data: { id: "request-id", status: "pending" } });

    await workingHoursRequestsApi.create(input);

    expect(httpMock.post).toHaveBeenCalledWith("/working-hours-requests", input);
  });

  it("passes the concurrency version when approving a request", async () => {
    const input = { expectedVersion: 2, message: "Согласовано" };
    httpMock.post.mockResolvedValue({ data: { id: "request-id", status: "approved" } });

    await workingHoursRequestsApi.approve("request-id", input);

    expect(httpMock.post).toHaveBeenCalledWith("/working-hours-requests/request-id/approve", input);
  });
});
