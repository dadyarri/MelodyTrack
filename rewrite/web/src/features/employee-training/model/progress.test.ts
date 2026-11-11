import { describe, expect, it } from "vitest";
import {
  startDemoSession,
  createProgress,
  finishDemoSession,
  readProgress,
  recordDemoStep,
} from "./progress";

describe("staff access training", () => {
  it("requires the demonstrated session to open before the exercise can finish", () => {
    expect(finishDemoSession("signed-out")).toBe("signed-out");
    const signedIn = startDemoSession("signed-out");

    expect(signedIn).toBe("signed-in");
    expect(finishDemoSession(signedIn)).toBe("completed");
  });

  it("keeps reading an article and deferring separate from completing an exercise", () => {
    const progress = { ...createProgress("staff-a"), articleRead: true, deferred: true };

    const restored = readProgress(progress, "staff-a");

    expect(restored.articleRead).toBe(true);
    expect(restored.deferred).toBe(true);
    expect(restored.demoStep).toBe("signed-out");
  });

  it("rejects progress belonging to another account", () => {
    const anotherAccount = { ...createProgress("staff-a"), demoStep: "completed" as const };

    expect(() => readProgress(anotherAccount, "staff-b")).toThrow();
    expect(createProgress("staff-b").demoStep).toBe("signed-out");
  });

  it("rejects future material formats instead of replacing saved progress", () => {
    const future = { ...createProgress("staff-a"), materialVersion: 2 };

    expect(() => readProgress(future, "staff-a")).toThrow();
  });

  it("preserves completed training and article history while replaying the demonstration", () => {
    const completed = {
      ...createProgress("staff-a"),
      demoStep: "completed" as const,
      articleRead: true,
      deferred: true,
    };

    const replayStart = recordDemoStep(completed, "signed-out");
    const signedIn = startDemoSession("signed-out");
    const replayInProgress = recordDemoStep(replayStart, signedIn);
    const replayComplete = recordDemoStep(replayInProgress, finishDemoSession(signedIn));

    expect(replayStart).toBe(completed);
    expect(replayInProgress).toBe(completed);
    expect(replayComplete).toBe(completed);
    expect(signedIn).toBe("signed-in");
    expect(finishDemoSession(signedIn)).toBe("completed");
  });
});
