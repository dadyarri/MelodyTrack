import { beforeEach, describe, expect, it, vi } from "vitest";

const httpMock = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
}));

vi.mock("@/shared/api", () => ({
  http: httpMock,
}));

import { vacationRequestsApi } from "./vacationRequestApi";

describe("vacationRequestsApi", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("uses the portal-owned submission endpoint without a client identifier", async () => {
    const input = { startDate: "2030-06-01", endDate: "2030-06-07", message: "Отпуск" };
    const response = { id: "request-id", status: "pending" };
    httpMock.post.mockResolvedValue({ data: response });

    await expect(vacationRequestsApi.create(input, true)).resolves.toEqual(response);

    expect(httpMock.post).toHaveBeenCalledWith("/client-portal/vacation-requests", input);
  });

  it("passes the concurrency version when approving a request", async () => {
    const input = { expectedVersion: 3, message: "Согласовано" };
    httpMock.post.mockResolvedValue({ data: { id: "request-id", status: "approved" } });

    await vacationRequestsApi.approve("request-id", input);

    expect(httpMock.post).toHaveBeenCalledWith("/vacation-requests/request-id/approve", input);
  });

  it("loads only the current portal client's requests with cancellation", async () => {
    const controller = new AbortController();
    httpMock.get.mockResolvedValue({ data: { items: [] } });

    await expect(vacationRequestsApi.listMine(true, controller.signal)).resolves.toEqual([]);

    expect(httpMock.get).toHaveBeenCalledWith("/client-portal/vacation-requests", { signal: controller.signal });
  });
});
