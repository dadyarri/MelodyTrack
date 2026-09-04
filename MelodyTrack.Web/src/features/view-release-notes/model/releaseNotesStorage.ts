import * as v from "valibot";

const releaseNotesSeenKeyPrefix = "melodytrack.releaseNotesSeen:";
const releaseVersionSchema = v.pipe(v.string(), v.regex(/^\d{4}\.(?:0[1-9]|1[0-2])\.[1-9]\d*(?:\.[1-9]\d*)?$/));

type ReleaseNotesStorage = Pick<Storage, "getItem" | "setItem">;

export function getReleaseNotesSeenKey(userId: string) {
  return `${releaseNotesSeenKeyPrefix}${userId}`;
}

export function parseReleaseNotesSeenVersion(value: string | null) {
  if (value === null) {
    return null;
  }

  const result = v.safeParse(releaseVersionSchema, value);
  return result.success ? result.output : null;
}

export function readReleaseNotesSeenVersion(userId: string, storage: ReleaseNotesStorage | null = getBrowserStorage()) {
  if (!userId || !storage) {
    return null;
  }

  try {
    return parseReleaseNotesSeenVersion(storage.getItem(getReleaseNotesSeenKey(userId)));
  } catch {
    return null;
  }
}

export function writeReleaseNotesSeenVersion(userId: string, version: string, storage: ReleaseNotesStorage | null = getBrowserStorage()) {
  if (!userId || !storage || parseReleaseNotesSeenVersion(version) === null) {
    return false;
  }

  try {
    storage.setItem(getReleaseNotesSeenKey(userId), version);
    return true;
  } catch {
    return false;
  }
}

function getBrowserStorage(): ReleaseNotesStorage | null {
  if (typeof window === "undefined") {
    return null;
  }

  try {
    return window.localStorage;
  } catch {
    return null;
  }
}
