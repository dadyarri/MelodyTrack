import { describe, expect, it } from "vitest";

import {
  getReleaseNotesSeenKey,
  parseReleaseNotesSeenVersion,
  readReleaseNotesSeenVersion,
  writeReleaseNotesSeenVersion,
} from "./releaseNotesStorage";

describe("release notes acknowledgement storage", () => {
  it("keeps acknowledgements separate for each user", () => {
    const storage = createStorage();

    expect(writeReleaseNotesSeenVersion("user-1", "2026.07.1", storage)).toBe(true);

    expect(readReleaseNotesSeenVersion("user-1", storage)).toBe("2026.07.1");
    expect(readReleaseNotesSeenVersion("user-2", storage)).toBeNull();
    expect(storage.getItem(getReleaseNotesSeenKey("user-1"))).toBe("2026.07.1");
  });

  it.each([null, "", "2026.7.1", "2026.07.0", "2026.07.1.0", "latest", "2026.13.1"])("rejects malformed stored version %s", (version) => {
    expect(parseReleaseNotesSeenVersion(version)).toBeNull();
  });

  it("accepts releases and patches", () => {
    expect(parseReleaseNotesSeenVersion("2026.07.1")).toBe("2026.07.1");
    expect(parseReleaseNotesSeenVersion("2026.07.1.2")).toBe("2026.07.1.2");
  });

  it("treats inaccessible storage as unseen without throwing", () => {
    const storage = createStorage();
    storage.getItem = () => {
      throw new DOMException("Storage blocked", "SecurityError");
    };
    storage.setItem = () => {
      throw new DOMException("Storage blocked", "SecurityError");
    };

    expect(readReleaseNotesSeenVersion("user-1", storage)).toBeNull();
    expect(writeReleaseNotesSeenVersion("user-1", "2026.07.1", storage)).toBe(false);
  });
});

function createStorage(): Storage {
  const values = new Map<string, string>();

  return {
    get length() {
      return values.size;
    },
    clear: () => {
      values.clear();
    },
    getItem: (key) => values.get(key) ?? null,
    key: (index) => [...values.keys()][index] ?? null,
    removeItem: (key) => values.delete(key),
    setItem: (key, value) => {
      values.set(key, value);
    },
  };
}
