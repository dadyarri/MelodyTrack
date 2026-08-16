import { beforeEach, describe, expect, it, vi } from "vitest";

const httpMock = vi.hoisted(() => ({
  delete: vi.fn(),
  get: vi.fn(),
  patch: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
}));

vi.mock("@/shared/api", () => ({
  http: httpMock,
}));

import { clientsApi } from "./clientApi";
import { clientQueryKeys } from "./queryKeys";

describe("clientsApi", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("passes list filters through the client endpoint", async () => {
    const response = {
      data: {
        data: [],
        info: {
          page: 2,
          pageSize: 10,
          total: 0,
          hasPrevPage: true,
          hasNextPage: false,
        },
      },
    };
    httpMock.get.mockResolvedValue(response);

    await expect(clientsApi.list({ page: 2, page_size: 10, search: "Иван" })).resolves.toBe(response.data);
    expect(httpMock.get).toHaveBeenCalledWith("/clients", {
      params: { page: 2, page_size: 10, search: "Иван" },
    });
  });

  it("preserves the replay key when creating a client", async () => {
    httpMock.post.mockResolvedValue({ data: { id: "client-1" } });

    await clientsApi.create(
      {
        firstName: "Иван",
        lastName: "Иванов",
      },
      { idempotencyKey: "replay-1" },
    );

    expect(httpMock.post).toHaveBeenCalledWith(
      "/clients",
      {
        firstName: "Иван",
        lastName: "Иванов",
      },
      {
        headers: {
          "Idempotency-Key": "replay-1",
        },
      },
    );
  });

  it("regenerates the calendar subscription through the client boundary", async () => {
    const subscription = {
      id: "subscription-1",
      token: "token",
      url: "https://example.test/calendar.ics",
      feedType: "client",
    };
    httpMock.post.mockResolvedValue({ data: subscription });

    await expect(clientsApi.regenerateCalendarSubscription("client-1")).resolves.toBe(subscription);
    expect(httpMock.post).toHaveBeenCalledWith("/clients/client-1/calendar-subscriptions", {});
  });

  it("preserves the existing query-key shape", () => {
    expect(clientQueryKeys.all).toEqual(["clients"]);
    expect(clientQueryKeys.list(2, "Иван")).toEqual(["clients", "list", 2, "Иван"]);
    expect(clientQueryKeys.history("client-1", 3, 8)).toEqual(["clients", "history", "client-1", 3, 8]);
    expect(clientQueryKeys.history()).toEqual(["clients", "history", null, null, null]);
  });
});
