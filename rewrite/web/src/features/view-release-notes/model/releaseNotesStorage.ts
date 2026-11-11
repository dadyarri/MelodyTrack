import { releaseVersionPattern } from "@/entities/app-release";

export function releaseNotesSeenKey(staffId: string): string {
  return `melodytrack.rewrite.releaseNotesSeen:${staffId}`;
}

export function readSeenVersion(staffId: string): string | null {
  try {
    const value = window.localStorage.getItem(releaseNotesSeenKey(staffId));
    return value && releaseVersionPattern.test(value) ? value : null;
  } catch {
    return null;
  }
}

export function writeSeenVersion(staffId: string, version: string): boolean {
  if (!staffId || !releaseVersionPattern.test(version)) return false;
  try {
    window.localStorage.setItem(releaseNotesSeenKey(staffId), version);
    return true;
  } catch {
    return false;
  }
}
