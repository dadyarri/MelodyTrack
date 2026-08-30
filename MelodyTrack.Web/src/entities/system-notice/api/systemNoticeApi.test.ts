import { beforeEach, describe, expect, it, vi } from "vitest";

const httpMock = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
}));

vi.mock("@/shared/api", () => ({
  http: httpMock,
}));

import { systemNoticeApi } from "./systemNoticeApi";

describe("systemNoticeApi", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("loads authenticated notices with request cancellation", async () => {
    const controller = new AbortController();
    httpMock.get.mockResolvedValue({ data: { items: [] } });

    await expect(systemNoticeApi.list(controller.signal)).resolves.toEqual([]);

    expect(httpMock.get).toHaveBeenCalledWith("/system-notices", { signal: controller.signal });
  });

  it("loads login-page notices without authentication refresh", async () => {
    httpMock.get.mockResolvedValue({ data: { items: [] } });

    await expect(systemNoticeApi.listPreAuth()).resolves.toEqual([]);

    expect(httpMock.get).toHaveBeenCalledWith("/system-notices/pre-auth", {
      signal: undefined,
      skipAuthRefresh: true,
    });
  });

  it("dismisses a notice through its recipient state endpoint", async () => {
    httpMock.post.mockResolvedValue({ data: undefined });

    await expect(systemNoticeApi.dismiss("notice-1")).resolves.toBeUndefined();

    expect(httpMock.post).toHaveBeenCalledWith("/system-notices/notice-1/dismissals", {});
  });
});
