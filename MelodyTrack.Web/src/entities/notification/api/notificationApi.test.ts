import { beforeEach, describe, expect, it, vi } from "vitest";

const httpMock = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
}));

vi.mock("@/shared/api", () => ({
  http: httpMock,
}));

import { notificationApi } from "./notificationApi";

describe("notificationApi", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("loads only unread notifications with request cancellation", async () => {
    const controller = new AbortController();
    const payload = { items: [], unreadCount: 0 };
    httpMock.get.mockResolvedValue({ data: payload });

    await expect(notificationApi.listUnread(controller.signal)).resolves.toEqual(payload);

    expect(httpMock.get).toHaveBeenCalledWith("/notifications", {
      params: { unreadOnly: true },
      signal: controller.signal,
    });
  });

  it("registers a browser subscription through the account-scoped endpoint", async () => {
    const request = { endpoint: "https://push.example/subscription", p256Dh: "key", auth: "auth" };
    httpMock.post.mockResolvedValue({ data: undefined });

    await expect(notificationApi.subscribe(request)).resolves.toBeUndefined();

    expect(httpMock.post).toHaveBeenCalledWith("/notifications/push/subscription", request);
  });

  it("marks all notifications read", async () => {
    httpMock.post.mockResolvedValue({ data: undefined });

    await expect(notificationApi.markAllRead()).resolves.toBeUndefined();

    expect(httpMock.post).toHaveBeenCalledWith("/notifications/read-all", {});
  });
});
