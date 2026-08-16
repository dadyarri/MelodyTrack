const chunkRetryKey = "melodytrack:chunk-retry-path";
const navigationIntentKey = "melodytrack:navigation-intent";

const recoverableChunkPatterns = [
  /failed to fetch dynamically imported module/i,
  /importing a module script failed/i,
  /loading chunk \d+ failed/i,
  /loading css chunk \d+ failed/i,
  /dynamically imported module/i,
];

export function isRecoverableChunkLoadError(error: unknown) {
  const message = getErrorMessage(error);
  return recoverableChunkPatterns.some((pattern) => pattern.test(message));
}

export async function recoverableImport<T>(load: () => Promise<T>) {
  try {
    const module = await load();
    clearChunkRetryMarker();
    return module;
  } catch (error) {
    if (!isRecoverableChunkLoadError(error)) {
      clearChunkRetryMarker();
      throw error;
    }

    if (markChunkRetryAttempt()) {
      window.location.assign(getRecoveryPath());
      return await new Promise<T>(() => {});
    }

    clearChunkRetryMarker();
    throw error;
  }
}

export function clearChunkRetryMarker() {
  if (typeof window === "undefined") {
    return;
  }

  window.sessionStorage.removeItem(chunkRetryKey);
  clearNavigationIntent();
}

export function rememberNavigationIntent(path: string) {
  if (typeof window === "undefined") {
    return;
  }

  window.sessionStorage.setItem(navigationIntentKey, path);
}

export function clearNavigationIntent(completedPath?: string) {
  if (typeof window === "undefined") {
    return;
  }

  if (completedPath && window.sessionStorage.getItem(navigationIntentKey) !== completedPath) {
    return;
  }

  window.sessionStorage.removeItem(navigationIntentKey);
}

function markChunkRetryAttempt() {
  if (typeof window === "undefined") {
    return false;
  }

  const currentPath = getRecoveryPath();
  const previousPath = window.sessionStorage.getItem(chunkRetryKey);

  if (previousPath === currentPath) {
    return false;
  }

  window.sessionStorage.setItem(chunkRetryKey, currentPath);
  return true;
}

function getRecoveryPath() {
  return window.sessionStorage.getItem(navigationIntentKey) ?? window.location.pathname + window.location.search;
}

function getErrorMessage(error: unknown) {
  if (error instanceof Error) {
    return error.message;
  }

  if (typeof error === "string") {
    return error;
  }

  return "";
}
