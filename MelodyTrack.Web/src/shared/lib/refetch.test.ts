import { describe, expect, it } from "vitest";

import { canPollInBackground, defaultQueryStaleTimeMs, getBackgroundRefetchInterval, referenceQueryStaleTimeMs } from "./refetch";

describe("query refresh policy", () => {
  it("defines normal and reference-data freshness windows", () => {
    expect(defaultQueryStaleTimeMs).toBe(30_000);
    expect(referenceQueryStaleTimeMs).toBe(300_000);
  });

  it.each([
    { paused: true, visibility: "visible" as const, online: true },
    { paused: false, visibility: "hidden" as const, online: true },
    { paused: false, visibility: "visible" as const, online: false },
  ])("pauses unavailable background work", ({ paused, visibility, online }) => {
    expect(canPollInBackground(paused, visibility, online)).toBe(false);
  });

  it("polls only while the screen is active, visible, and online", () => {
    expect(canPollInBackground(false, "visible", true)).toBe(true);
    expect(getBackgroundRefetchInterval(false, 12_000)).toBe(12_000);
    expect(getBackgroundRefetchInterval(true, 12_000)).toBe(false);
  });
});
