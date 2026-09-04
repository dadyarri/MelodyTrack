import { describe, expect, it } from "vitest";

import { getDraftGuardState } from "./useUnsavedDraftGuard";

describe("draft navigation guard", () => {
  it.each([
    [false, "failed", false, false],
    [true, "loading", false, false],
    [true, "saved", false, false],
    [true, "saving", true, true],
    [true, "failed", true, true],
  ] as const)("derives guards for active=%s and status=%s", (isActive, status, blockClientNavigation, warnBeforeUnload) => {
    expect(getDraftGuardState(isActive, status)).toEqual({ blockClientNavigation, warnBeforeUnload });
  });
});
