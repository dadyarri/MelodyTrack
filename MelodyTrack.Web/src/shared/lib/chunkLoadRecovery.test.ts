import { beforeEach, describe, expect, it } from "vitest";

import { clearNavigationIntent, isRecoverableChunkLoadError, recoverableImport, rememberNavigationIntent } from "./chunkLoadRecovery";

const navigationIntentKey = "melodytrack:navigation-intent";

describe("chunk load recovery", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it("recognizes dynamic import failures", () => {
    expect(isRecoverableChunkLoadError(new TypeError("Failed to fetch dynamically imported module"))).toBe(true);
    expect(isRecoverableChunkLoadError(new Error("Request failed with status 500"))).toBe(false);
  });

  it("keeps a pending navigation intent until that route completes", () => {
    rememberNavigationIntent("/courses");

    clearNavigationIntent("/clients");
    expect(window.sessionStorage.getItem(navigationIntentKey)).toBe("/courses");

    clearNavigationIntent("/courses");
    expect(window.sessionStorage.getItem(navigationIntentKey)).toBeNull();
  });

  it("clears recovery state after a lazy import succeeds", async () => {
    rememberNavigationIntent("/schedule");

    await expect(recoverableImport(() => Promise.resolve({ page: "schedule" }))).resolves.toEqual({ page: "schedule" });
    expect(window.sessionStorage.getItem(navigationIntentKey)).toBeNull();
  });
});
