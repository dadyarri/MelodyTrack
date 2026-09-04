export const defaultQueryStaleTimeMs = 30_000;
export const referenceQueryStaleTimeMs = 5 * 60_000;

export function canPollInBackground(
  isPaused: boolean,
  visibilityState = typeof document === "undefined" ? "visible" : document.visibilityState,
  isOnline = typeof navigator === "undefined" ? true : navigator.onLine,
) {
  return !isPaused && visibilityState === "visible" && isOnline;
}

export function getBackgroundRefetchInterval(isPaused: boolean, intervalMs = 5000) {
  return canPollInBackground(isPaused) ? intervalMs : false;
}
