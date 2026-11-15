import { ResponseHandlerOption } from "@microsoft/kiota-abstractions";
import { apiOptions, type applicationApi, invalidResponse, isAbortError, toApiError } from "./http";

const listeners = new Set<() => void>();

export function subscribeDataChanges(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function emitDataChange() {
  for (const listener of listeners) listener();
}

/** Keep the generated route, bearer middleware and safe error mapping, but do not buffer SSE. */
export async function openDataChanges(
  api: ReturnType<typeof applicationApi>,
  signal: AbortSignal,
): Promise<Response> {
  let response: Response | undefined;
  const handler = new ResponseHandlerOption();
  handler.responseHandler = {
    async handleResponse<T, R>(value: T): Promise<R | undefined> {
      if (!(value instanceof Response)) throw invalidResponse();
      response = value;
      return undefined;
    },
  };
  await api.changes.events.get({
    headers: { Accept: "text/event-stream" },
    options: [...apiOptions(signal).options, handler],
  });
  if (
    !response?.body ||
    response.headers.get("content-type")?.split(";")[0]?.trim().toLowerCase() !==
      "text/event-stream"
  ) {
    await response?.body?.cancel();
    throw invalidResponse();
  }
  return response;
}

/** Only invalidations are accepted. No event payload becomes client state. */
export async function readDataChanges(
  response: Response,
  signal: AbortSignal,
  onChange: () => void | Promise<void>,
  onActivity: () => void = () => {},
): Promise<void> {
  if (!response.body) throw invalidResponse();
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let line = "";
  let event = "";
  let hasData = false;
  let carriageReturn = false;
  const finishLine = async () => {
    if (!line) {
      if (!signal.aborted && event === "invalidate" && hasData) await onChange();
      event = "";
      hasData = false;
    } else if (line.startsWith("event:")) {
      event = line.slice(6).replace(/^ /, "");
    } else if (line === "data" || line.startsWith("data:")) {
      hasData = true;
    }
    line = "";
  };
  const cancel = () => {
    void reader.cancel().catch(() => {});
  };
  signal.addEventListener("abort", cancel, { once: true });
  try {
    while (!signal.aborted) {
      const { done, value } = await reader.read();
      if (done || signal.aborted) return;
      onActivity();
      for (const character of decoder.decode(value, { stream: true })) {
        if (character === "\n" && carriageReturn) {
          carriageReturn = false;
          continue;
        }
        carriageReturn = character === "\r";
        if (character === "\r" || character === "\n") await finishLine();
        else {
          line += character;
          if (line.length > 16_384) throw invalidResponse();
        }
      }
    }
  } finally {
    signal.removeEventListener("abort", cancel);
    await reader.cancel().catch(() => {});
    reader.releaseLock();
  }
}

/** Retry the stream only; ordinary API reads are driven by server invalidations, never a poll. */
export function startDataChanges(
  open: (signal: AbortSignal) => Promise<Response>,
  onChange: () => void | Promise<void> = emitDataChange,
): () => void {
  let stopped = false;
  let active: AbortController | null = null;
  let retry: ReturnType<typeof setTimeout> | undefined;
  let watchdog: ReturnType<typeof setTimeout> | undefined;
  let delay = 1000;
  let focused = true;
  const watch = () => {
    clearTimeout(watchdog);
    const controller = active;
    // Background tabs may suspend reads and timers. Silence there is not a broken connection.
    if (!controller || !focused || document.visibilityState !== "visible") return;
    watchdog = setTimeout(() => {
      if (focused && document.visibilityState === "visible") controller.abort();
    }, 45_000);
  };
  const connect = async () => {
    if (stopped || active || !navigator.onLine) return;
    clearTimeout(retry);
    const controller = new AbortController();
    active = controller;
    const connectedAt = Date.now();
    const activity = () => {
      if (Date.now() - connectedAt >= 30_000) delay = 1000;
      watch();
    };
    activity();
    let terminal = false;
    try {
      const response = await open(controller.signal);
      controller.signal.throwIfAborted();
      await readDataChanges(response, controller.signal, onChange, activity);
    } catch (error) {
      const failure = toApiError(error);
      const status = failure.status;
      if (failure.retryAfterSeconds) delay = Math.max(delay, failure.retryAfterSeconds * 1000);
      terminal = !isAbortError(error) && (status === 401 || status === 403);
    } finally {
      clearTimeout(watchdog);
      controller.abort();
      if (active === controller) active = null;
      if (!stopped && !terminal) {
        retry = setTimeout(() => {
          void connect();
        }, delay);
        delay = Math.min(delay * 2, 30_000);
      }
    }
  };
  const resume = () => {
    // Give the existing reader a full heartbeat deadline after browser suspension.
    watch();
    if (focused && document.visibilityState === "visible") void connect();
  };
  const focus = () => {
    focused = true;
    resume();
  };
  const blur = () => {
    focused = false;
    watch();
  };
  window.addEventListener("focus", focus);
  window.addEventListener("blur", blur);
  window.addEventListener("online", resume);
  document.addEventListener("visibilitychange", resume);
  void connect();
  return () => {
    stopped = true;
    clearTimeout(retry);
    clearTimeout(watchdog);
    active?.abort();
    window.removeEventListener("focus", focus);
    window.removeEventListener("blur", blur);
    window.removeEventListener("online", resume);
    document.removeEventListener("visibilitychange", resume);
  };
}
