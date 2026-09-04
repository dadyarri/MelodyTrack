import { beforeEach, describe, expect, it } from "vitest";

import { type SavedClientIdentity, savedClientStorage } from "./savedClientStorage";

const identity: SavedClientIdentity = {
  identityId: "client-1",
  reference: "opaque-reference",
  displayLabel: "Анна К.",
  lastUsedAtUtc: "2026-07-31T12:00:00.000Z",
};

beforeEach(() => {
  localStorage.clear();
});

describe("saved client storage", () => {
  it("stores a versioned non-secret identity and replaces rotations for the same client", () => {
    expect(savedClientStorage.remember(identity)).toBe(true);
    expect(savedClientStorage.remember({ ...identity, reference: "rotated-reference", lastUsedAtUtc: "2026-07-31T13:00:00.000Z" })).toBe(
      true,
    );

    expect(savedClientStorage.read()).toEqual([{ ...identity, reference: "rotated-reference", lastUsedAtUtc: "2026-07-31T13:00:00.000Z" }]);
    expect(localStorage.getItem("melodytrack.savedClientIdentities")).not.toContain("PIN");
  });

  it("discards malformed records and can forget one identity", () => {
    localStorage.setItem("melodytrack.savedClientIdentities", JSON.stringify({ version: 1, identities: [{ token: "secret-link" }] }));
    expect(savedClientStorage.read()).toEqual([]);
    expect(localStorage.getItem("melodytrack.savedClientIdentities")).toBeNull();

    savedClientStorage.remember(identity);
    expect(savedClientStorage.forget(identity.identityId)).toBe(true);
    expect(savedClientStorage.read()).toEqual([]);
  });
});
