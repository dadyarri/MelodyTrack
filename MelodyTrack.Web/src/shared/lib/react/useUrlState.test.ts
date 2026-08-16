import { describe, expect, it } from "vitest";

import { readPositiveInteger, updateUrlSearchParams } from "./useUrlState";

describe("URL state", () => {
  it.each([
    ["3", 3],
    ["0", 1],
    ["-2", 1],
    ["abc", 1],
    [null, 1],
  ])("reads a safe positive page from %s", (value, expected) => {
    expect(readPositiveInteger(value)).toBe(expected);
  });

  it("updates only owned values and removes defaults", () => {
    const current = new URLSearchParams("tab=rules&page=2&q=Ada");
    const next = updateUrlSearchParams(current, { page: null, q: "Grace", provider: undefined });

    expect(next.toString()).toBe("tab=rules&q=Grace");
    expect(current.toString()).toBe("tab=rules&page=2&q=Ada");
  });

  it("preserves unrelated state while navigating the schedule", () => {
    const current = new URLSearchParams("week=2026-07-20&provider=user-1&create=appointment");
    const next = updateUrlSearchParams(current, { week: "2026-07-27", provider: null });

    expect(next.toString()).toBe("week=2026-07-27&create=appointment");
    expect(current.toString()).toBe("week=2026-07-20&provider=user-1&create=appointment");
  });
});
