import { describe, expect, it } from "vitest";

import type { AppUser } from "@/entities/session";

import { canViewReleaseNotes } from "./releaseNotesAccess";

describe("release notes access", () => {
  it.each([
    ["regular user", { isAdmin: false, isSuperuser: false }, false],
    ["admin", { isAdmin: true, isSuperuser: false }, true],
    ["superuser", { isAdmin: false, isSuperuser: true }, true],
  ])("allows the expected visibility for a %s", (_, flags, expected) => {
    expect(canViewReleaseNotes(flags as AppUser)).toBe(expected);
  });
});
