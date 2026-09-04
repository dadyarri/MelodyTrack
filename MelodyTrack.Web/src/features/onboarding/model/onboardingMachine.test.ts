import { describe, expect, it } from "vitest";

import {
  canShowOnboardingStep,
  initialOnboardingMachineState,
  isLatestOnboardingRequest,
  reduceOnboardingMachine,
} from "./onboardingMachine";

describe("onboarding state machine", () => {
  it("waits for navigation and a target before showing a step", () => {
    const navigating = reduceOnboardingMachine(initialOnboardingMachineState, { type: "route-required" });
    const locating = reduceOnboardingMachine(navigating, { type: "target-required" });
    const ready = reduceOnboardingMachine(locating, { type: "target-found" });

    expect(navigating.phase).toBe("navigating");
    expect(locating.phase).toBe("locating");
    expect(ready.phase).toBe("ready");
  });

  it("does not show the next hint before saving, navigation, and target discovery finish", () => {
    const saving = reduceOnboardingMachine(initialOnboardingMachineState, {
      type: "operation-started",
      action: "progress",
      requestId: 1,
    });
    const ready = reduceOnboardingMachine(saving, { type: "operation-succeeded", requestId: 1 });

    expect(canShowOnboardingStep({ phase: saving.phase, currentPath: "/", stepPath: "/tasks", hasTarget: false })).toBe(false);
    expect(canShowOnboardingStep({ phase: ready.phase, currentPath: "/", stepPath: "/tasks", hasTarget: false })).toBe(false);
    expect(canShowOnboardingStep({ phase: ready.phase, currentPath: "/tasks", stepPath: "/tasks", hasTarget: false })).toBe(false);
    expect(canShowOnboardingStep({ phase: ready.phase, currentPath: "/tasks", stepPath: "/tasks", hasTarget: true })).toBe(true);
  });

  it("keeps a failed progress save retryable", () => {
    const saving = reduceOnboardingMachine(initialOnboardingMachineState, {
      type: "operation-started",
      action: "progress",
      requestId: 3,
    });
    const failed = reduceOnboardingMachine(saving, { type: "operation-failed", action: "progress", requestId: 3 });
    const retrying = reduceOnboardingMachine(failed, { type: "retry" });

    expect(failed).toMatchObject({ phase: "failed", retryAction: "progress" });
    expect(retrying.phase).toBe("saving");
  });

  it("ignores a stale response after a newer progress request", () => {
    const savingLatest = reduceOnboardingMachine(initialOnboardingMachineState, {
      type: "operation-started",
      action: "progress",
      requestId: 5,
    });

    expect(reduceOnboardingMachine(savingLatest, { type: "operation-succeeded", requestId: 4 })).toBe(savingLatest);
    expect(isLatestOnboardingRequest(4, 5)).toBe(false);
    expect(isLatestOnboardingRequest(5, 5)).toBe(true);
  });

  it.each(["complete", "skip"] as const)("closes after a successful %s operation", (action) => {
    const finishing = reduceOnboardingMachine(initialOnboardingMachineState, {
      type: "operation-started",
      action,
      requestId: 7,
    });
    const closed = reduceOnboardingMachine(finishing, { type: "operation-succeeded", requestId: 7, closesTour: true });

    expect(finishing.phase).toBe("finishing");
    expect(closed).toEqual(initialOnboardingMachineState);
  });
});
