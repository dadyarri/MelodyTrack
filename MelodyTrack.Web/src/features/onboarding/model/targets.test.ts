import { afterEach, describe, expect, it, vi } from "vitest";

import { findOnboardingTarget, getMissingTargetRecoveryIndex } from "./targets";

afterEach(() => {
  document.body.replaceChildren();
});

describe("onboarding targets", () => {
  it("uses a visible target and ignores a hidden responsive duplicate", () => {
    const hidden = document.createElement("div");
    hidden.dataset.onboardingId = "dashboard-content";
    hidden.style.display = "none";
    const visible = document.createElement("div");
    visible.dataset.onboardingId = "dashboard-content";
    vi.spyOn(visible, "getClientRects").mockReturnValue({ length: 1 } as DOMRectList);
    document.body.append(hidden, visible);

    expect(findOnboardingTarget("dashboard-content")).toBe(visible);
  });

  it("returns no target when the current layout does not render it", () => {
    expect(findOnboardingTarget("courses-workspace")).toBeNull();
  });

  it("skips a missing target or finishes when no steps remain", () => {
    expect(getMissingTargetRecoveryIndex(1, 4)).toBe(2);
    expect(getMissingTargetRecoveryIndex(3, 4)).toBeNull();
  });
});
