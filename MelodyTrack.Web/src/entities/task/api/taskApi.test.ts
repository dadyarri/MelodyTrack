import { beforeEach, describe, expect, it, vi } from "vitest";

const httpMock = vi.hoisted(() => ({
  get: vi.fn(),
  patch: vi.fn(),
  post: vi.fn(),
}));

vi.mock("@/shared/api", () => ({
  http: httpMock,
}));

import { tasksApi } from "./taskApi";

describe("tasksApi", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    httpMock.post.mockResolvedValue({ data: undefined });
  });

  it.each([
    ["complete", "completion", {}],
    ["cancel", "cancellation", {}],
    ["delay", "deferral", { delayUntilUtc: "2026-07-27T12:00:00.000Z" }],
  ] as const)("puts task identity in the URI for %s", async (method, transition, extra) => {
    const input = {
      timezone: "Europe/Moscow",
      ruleId: "rule-1",
      type: "appointment-reminder" as const,
      deduplicationKey: "appointment/reminder:2026-07-27",
      appointmentId: "appointment-1",
      ...extra,
    };

    if (method === "delay") {
      await tasksApi.delay({ ...input, delayUntilUtc: extra.delayUntilUtc });
    } else if (method === "complete") {
      await tasksApi.complete(input);
    } else {
      await tasksApi.cancel(input);
    }

    expect(httpMock.post).toHaveBeenCalledWith(`/tasks/appointment%2Freminder%3A2026-07-27/${transition}`, {
      timezone: "Europe/Moscow",
      ruleId: "rule-1",
      type: "appointment-reminder",
      appointmentId: "appointment-1",
      ...extra,
    });
  });
});
