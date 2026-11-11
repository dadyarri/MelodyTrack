import { afterEach, describe, expect, it, vi } from "vitest";
import { readSeenVersion, releaseNotesSeenKey, writeSeenVersion } from "./releaseNotesStorage";

afterEach(() => { window.localStorage.clear(); vi.restoreAllMocks(); });

describe("release notes acknowledgement", () => {
  it("belongs to the account that closed the notes", () => {
    expect(writeSeenVersion("staff-a", "2026.12.1")).toBe(true);

    expect(readSeenVersion("staff-a")).toBe("2026.12.1");
    expect(readSeenVersion("staff-b")).toBeNull();
  });

  it("ignores invalid persisted versions", () => {
    window.localStorage.setItem(releaseNotesSeenKey("staff-a"), "not-a-version");

    expect(readSeenVersion("staff-a")).toBeNull();
  });

  it("keeps storage failure out of the application error flow", () => {
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new DOMException("unavailable", "SecurityError"); });
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new DOMException("unavailable", "SecurityError"); });

    expect(writeSeenVersion("staff-a", "2026.12.1")).toBe(false);
    expect(readSeenVersion("staff-a")).toBeNull();
  });
});
