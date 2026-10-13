import { beforeEach, describe, expect, it, vi } from "vitest";
const httpMock = vi.hoisted(() => ({ get: vi.fn(), patch: vi.fn(), post: vi.fn() }));
vi.mock("@/shared/api", () => ({ http: httpMock }));
import { vacationsApi } from "./vacationApi";

describe("vacationsApi", () => {
  beforeEach(() => vi.clearAllMocks());
  it("loads the owner's vacation history with cancellation", async () => {
    const controller = new AbortController();
    httpMock.get.mockResolvedValue({ data: { items: [] } });
    await vacationsApi.list({ subjectType: "staff", subjectId: "teacher" }, controller.signal);
    expect(httpMock.get).toHaveBeenCalledWith("/vacations", {
      params: { subjectType: "staff", subjectId: "teacher", includeHistory: true },
      signal: controller.signal,
    });
  });
  it("retains the vacation identifier and version when editing", async () => {
    const input = { expectedVersion: 2, startDate: "2030-06-03T09:00:00Z", endDate: "2030-06-04T18:00:00Z" };
    httpMock.patch.mockResolvedValue({ data: { requiresApproval: true } });
    await expect(vacationsApi.update("vacation", input)).resolves.toEqual({ requiresApproval: true });
    expect(httpMock.patch).toHaveBeenCalledWith("/vacations/vacation", input);
  });
  it("restores only explicitly selected interruption identifiers", async () => {
    httpMock.post.mockResolvedValue({});
    await vacationsApi.restore("vacation", { expectedVersion: 3, interruptionIds: ["selected"] });
    expect(httpMock.post).toHaveBeenCalledWith("/vacations/vacation/restore-appointments", {
      expectedVersion: 3,
      interruptionIds: ["selected"],
    });
  });
});
