import { describe, expect, it } from "vitest";
import { createProgress, mergeTrainingProgress, readProgress } from "./progress";

describe("employee training progress compatibility", () => {
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

  it("adds the client exercise to older progress without resetting completed access training", () => {
    const older = {
      schemaVersion: 1,
      materialVersion: 1,
      staffId: "staff-a",
      demoStep: "completed",
      articleRead: true,
      deferred: true,
    };

    const restored = readProgress(older, "staff-a");

    expect(restored.demoStep).toBe("completed");
    expect(restored.articleRead).toBe(true);
    expect(restored.deferred).toBe(true);
    expect(restored.clientDirectory).toEqual({ version: 1, completed: false, deferred: false });
  });

  it("rejects future client exercises instead of overwriting their progress", () => {
    const future = {
      ...createProgress("staff-a"),
      clientDirectory: { version: 2, completed: true, deferred: false },
    };

    expect(() => readProgress(future, "staff-a")).toThrow();
  });

  it("preserves completed exercises and explicit deferrals when a stale tab saves", () => {
    const completed = {
      ...createProgress("staff-a"),
      demoStep: "completed" as const,
      articleRead: true,
      clientDirectory: { version: 1 as const, completed: true, deferred: true },
    };
    const stale = { ...createProgress("staff-a"), deferred: true };

    const merged = mergeTrainingProgress(completed, stale);

    expect(merged.demoStep).toBe("completed");
    expect(merged.articleRead).toBe(true);
    expect(merged.deferred).toBe(true);
    expect(merged.clientDirectory).toEqual({ version: 1, completed: true, deferred: true });
  });
});
