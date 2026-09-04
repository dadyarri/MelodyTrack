import { describe, expect, it } from "vitest";

import type { AppUser } from "@/entities/session";

import { buildNavigationTarget, getAvailableNavItems } from "./navigation";

describe("statistics navigation", () => {
  it.each([
    ["regular user", { isAdmin: false, isSuperuser: false, isClientPortal: false }, false],
    ["admin", { isAdmin: true, isSuperuser: false, isClientPortal: false }, true],
    ["superuser", { isAdmin: false, isSuperuser: true, isClientPortal: false }, true],
    ["client", { isAdmin: false, isSuperuser: false, isClientPortal: true }, false],
  ])("shows the three report areas to a %s when appropriate", (_, flags, expected) => {
    const reportItems = getAvailableNavItems(flags as AppUser).filter((item) => item.group === "stats");

    expect(reportItems.map((item) => item.key)).toEqual(expected ? ["/statistics/work", "/statistics/finance", "/statistics/clients"] : []);
  });

  it("preserves filters while moving between report areas", () => {
    expect(
      buildNavigationTarget("/statistics/finance", "/statistics/work", "?start=2026-07-01&end=2026-07-31&providerId=user-1&groupBy=month"),
    ).toBe("/statistics/finance?start=2026-07-01&end=2026-07-31&providerId=user-1&groupBy=month");
  });

  it("does not carry filters into statistics or unrelated pages", () => {
    expect(buildNavigationTarget("/statistics/work", "/schedule", "?date=2026-07-30")).toBe("/statistics/work");
    expect(buildNavigationTarget("/schedule", "/statistics/work", "?groupBy=month")).toBe("/schedule");
  });
});
