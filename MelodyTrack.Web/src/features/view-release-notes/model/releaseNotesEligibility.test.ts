import { describe, expect, it } from "vitest";

import { isReleaseNotesEligiblePath } from "./releaseNotesEligibility";

describe("release notes route eligibility", () => {
  it.each(["/login", "/restore", "/invite/code", "/portal", "/portal/access", "/portal/schedule"])("excludes %s", (pathname) => {
    expect(isReleaseNotesEligiblePath(pathname)).toBe(false);
  });

  it.each(["/", "/schedule", "/profile", "/clients"])("allows the staff route %s", (pathname) => {
    expect(isReleaseNotesEligiblePath(pathname)).toBe(true);
  });
});
