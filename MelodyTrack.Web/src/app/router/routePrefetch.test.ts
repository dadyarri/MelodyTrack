import { describe, expect, it, vi } from "vitest";

import { createRoutePrefetcher } from "./routePrefetch";

describe("route prefetch", () => {
  it("loads a known route once across repeated pointer and focus intent", async () => {
    const loadSchedule = vi.fn(() => Promise.resolve({}));
    const prefetch = createRoutePrefetcher({ "/schedule": loadSchedule });

    await Promise.all([prefetch("/schedule", undefined), prefetch("/schedule?week=2026-07-27", undefined)]);

    expect(loadSchedule).toHaveBeenCalledOnce();
  });

  it("ignores unknown routes and allows retry after a failed speculative request", async () => {
    const loadCourses = vi.fn().mockRejectedValueOnce(new Error("offline")).mockResolvedValueOnce({});
    const prefetch = createRoutePrefetcher({ "/courses": loadCourses });

    await expect(prefetch("/missing", undefined)).resolves.toBeUndefined();
    await expect(prefetch("/courses", undefined)).resolves.toBeUndefined();
    await expect(prefetch("/courses", undefined)).resolves.toBeUndefined();

    expect(loadCourses).toHaveBeenCalledTimes(2);
  });

  it("warms route data once after its module loads", async () => {
    const module = { route: "dashboard" };
    const prepare = vi.fn(() => Promise.resolve());
    const prefetch = createRoutePrefetcher<string>({ "/": () => Promise.resolve(module) }, prepare);

    await Promise.all([prefetch("/", "query-client"), prefetch("/", "query-client")]);

    expect(prepare).toHaveBeenCalledOnce();
    expect(prepare).toHaveBeenCalledWith(module, "query-client");
  });
});
