import {
  type AuthenticationProvider,
  HttpMethod,
  RequestInformation,
  type RequestOption,
  type ResponseHandler,
  ResponseHandlerOption,
} from "@microsoft/kiota-abstractions";
import { FetchRequestAdapter, HttpClient, type Middleware } from "@microsoft/kiota-http-fetchlibrary";

import { apiBaseUrl } from "../config";

export type HttpSession = {
  clear: () => void;
  getAccessToken: () => string | null;
  hasSession: () => boolean;
  setAccessToken: (accessToken: string) => void;
};

export type ApiValidationError = {
  path: string;
  code: string;
  message: string;
};

export type ApiProblemDetails = {
  type: string;
  title: string;
  status: number;
  detail?: string;
  instance: string;
  code: string;
  traceId: string;
  errors: ApiValidationError[];
  [key: string]: unknown;
};

export type StaleEntityConflict<TActivity = unknown> = ApiProblemDetails & {
  entityType: string;
  entityId: string;
  currentActivity?: TActivity | null;
};

export type AppErrorKind = "canceled" | "network" | "authentication" | "http" | "unknown";

export class AppError extends Error {
  readonly kind: AppErrorKind;
  readonly status?: number;
  readonly problem?: ApiProblemDetails;

  constructor(message: string, options: { kind: AppErrorKind; status?: number; problem?: ApiProblemDetails; cause?: unknown }) {
    super(message, { cause: options.cause });
    this.name = "AppError";
    this.kind = options.kind;
    this.status = options.status;
    this.problem = options.problem;
  }

  get traceId() {
    return this.problem?.traceId;
  }
}

type HttpRequestConfig = {
  data?: unknown;
  headers?: Record<string, string>;
  params?: object;
  responseType?: "blob";
  signal?: AbortSignal;
  validateStatus?: (status: number) => boolean;
  skipAuthRefresh?: boolean;
};

type HttpResponse<T> = {
  data: T;
  headers?: Headers;
  status?: number;
};

export const authExpiredEventName = "melodytrack:auth-expired";

const legacyCacheStorageKeyPrefix = "melodytrack:http-cache:";
const refreshChannelName = "melodytrack:session-refresh";
const csrfCookieName = "MelodyTrack.Csrf";
const csrfHeaderName = "X-CSRF-Token";
const publicAuthPaths = [
  "/client-portal/auth/link",
  "/auth/login",
  "/auth/register",
  "/auth/invites",
  "/auth/2fa/verify",
  "/auth/2fa/recover",
  "/auth/password-reset",
];

let refreshRequest: Promise<string | null> | null = null;
let httpSession: HttpSession | null = null;
let authExpiryPublished = false;
let lastSharedAccessToken: { accessToken: string; receivedAt: number } | null = null;

const refreshChannel =
  typeof window === "undefined" || typeof window.BroadcastChannel === "undefined" ? null : new window.BroadcastChannel(refreshChannelName);
refreshChannel?.addEventListener("message", (event: MessageEvent<unknown>) => {
  if (!isSharedRefreshMessage(event.data) || !httpSession?.hasSession()) {
    return;
  }

  lastSharedAccessToken = { accessToken: event.data.accessToken, receivedAt: Date.now() };
  httpSession.setAccessToken(event.data.accessToken);
  authExpiryPublished = false;
});

if (typeof window !== "undefined") {
  discardLegacyHttpCache(window.localStorage);
}

class MelodyTrackAuthenticationProvider implements AuthenticationProvider {
  authenticateRequest(request: RequestInformation) {
    const accessToken = httpSession?.getAccessToken();
    if (accessToken) {
      request.headers.add("Authorization", `Bearer ${accessToken}`);
    }

    return Promise.resolve();
  }
}

class MelodyTrackRequestOption implements RequestOption {
  static readonly key = "MelodyTrackRequestOption";

  constructor(
    readonly signal?: AbortSignal,
    readonly skipAuthRefresh = false,
  ) {}

  getKey() {
    return MelodyTrackRequestOption.key;
  }
}

class SessionRefreshMiddleware implements Middleware {
  next: Middleware | undefined;

  async execute(url: string, requestInit: RequestInit, requestOptions?: Record<string, RequestOption>) {
    if (!this.next) {
      throw new Error("Session refresh middleware is missing its fetch successor.");
    }

    const option = requestOptions?.[MelodyTrackRequestOption.key] as MelodyTrackRequestOption | undefined;
    const init = withBrowserRequestOptions(url, requestInit, option);
    const response = await this.next.execute(url, init, requestOptions);
    if (response.status !== 401 || option?.skipAuthRefresh || isPublicAuthUrl(url) || isRefreshUrl(url) || !httpSession?.hasSession()) {
      return response;
    }

    const accessToken = await getSharedRefreshRequest();
    if (!accessToken) {
      publishAuthExpiry();
      return response;
    }

    await response.body?.cancel().catch(() => undefined);
    const headers = new Headers(init.headers);
    headers.set("Authorization", `Bearer ${accessToken}`);
    return this.next.execute(url, { ...init, headers }, requestOptions);
  }
}

const sessionRefreshMiddleware = new SessionRefreshMiddleware();
const httpClient = new HttpClient((url, init) => fetch(url, init), sessionRefreshMiddleware);
export const requestAdapter = new FetchRequestAdapter(new MelodyTrackAuthenticationProvider(), undefined, undefined, httpClient);
requestAdapter.baseUrl = resolveAdapterBaseUrl();

export const http = {
  get<T>(path: string, config?: HttpRequestConfig) {
    return sendRequest<T>(HttpMethod.GET, path, undefined, config);
  },
  post<T>(path: string, body?: unknown, config?: HttpRequestConfig) {
    return sendRequest<T>(HttpMethod.POST, path, body, config);
  },
  put<T>(path: string, body?: unknown, config?: HttpRequestConfig) {
    return sendRequest<T>(HttpMethod.PUT, path, body, config);
  },
  patch<T>(path: string, body?: unknown, config?: HttpRequestConfig) {
    return sendRequest<T>(HttpMethod.PATCH, path, body, config);
  },
  delete<T>(path: string, config?: HttpRequestConfig) {
    return sendRequest<T>(HttpMethod.DELETE, path, config?.data, config);
  },
};

export function configureHttpSession(session: HttpSession) {
  httpSession = session;
  authExpiryPublished = false;
}

export function isHttpRequestCanceled(error: unknown) {
  return normalizeAppError(error).kind === "canceled";
}

export function restoreAccessToken() {
  const accessToken = httpSession?.getAccessToken();
  if (accessToken) {
    return Promise.resolve(accessToken);
  }

  return getSharedRefreshRequest();
}

export function discardLegacyHttpCache(storage: Storage) {
  const keysToRemove: string[] = [];
  for (let index = 0; index < storage.length; index += 1) {
    const key = storage.key(index);
    if (key?.startsWith(legacyCacheStorageKeyPrefix)) {
      keysToRemove.push(key);
    }
  }
  for (const key of keysToRemove) {
    storage.removeItem(key);
  }
}

export async function probeBackendReachable() {
  const accessToken = httpSession?.getAccessToken();
  if (!accessToken) {
    return false;
  }

  const controller = new AbortController();
  const timeoutId = window.setTimeout(() => {
    controller.abort();
  }, 3000);
  try {
    await http.get("/auth/me", {
      signal: controller.signal,
      skipAuthRefresh: true,
      validateStatus: () => true,
    });
    return true;
  } catch {
    return false;
  } finally {
    window.clearTimeout(timeoutId);
  }
}

export function normalizeAppError(error: unknown) {
  if (error instanceof AppError) {
    return error;
  }
  if (error instanceof DOMException && error.name === "AbortError") {
    return new AppError("Запрос отменён.", { kind: "canceled", cause: error });
  }
  if (error instanceof TypeError) {
    return new AppError("Не удалось подключиться к серверу. Проверьте соединение или попробуйте позже.", {
      kind: "network",
      cause: error,
    });
  }
  if (isApiProblemDetails(error)) {
    return new AppError(error.detail ?? error.title, {
      kind: error.status === 401 ? "authentication" : "http",
      status: error.status,
      problem: error,
      cause: error,
    });
  }
  if (error instanceof Error) {
    return new AppError(error.message, { kind: "unknown", cause: error });
  }
  return new AppError("Произошла неизвестная ошибка", { kind: "unknown", cause: error });
}

export function getApiProblemDetails(error: unknown) {
  return normalizeAppError(error).problem ?? null;
}

export function getApiFieldErrors(error: unknown) {
  const problem = getApiProblemDetails(error);
  const errorsByField: Record<string, string[]> = {};
  for (const validationError of problem?.errors ?? []) {
    const key = validationError.path.toLowerCase();
    errorsByField[key] ??= [];
    errorsByField[key].push(validationError.message);
  }
  return errorsByField;
}

export function getApiErrorMessages(error: unknown) {
  const appError = normalizeAppError(error);
  if (appError.kind === "network") {
    return ["Не удалось подключиться к серверу. Проверьте соединение или попробуйте позже."];
  }

  const problem = appError.problem;
  if (!problem) {
    return [appError.message];
  }

  const messages = new Set<string>();
  for (const validationError of problem.errors) {
    messages.add(validationError.message);
  }
  if (problem.detail) {
    messages.add(problem.detail);
  }
  if (messages.size === 0) {
    messages.add(problem.title);
  }
  return [[...messages].join("\n")];
}

export function getApiErrorMessage(error: unknown) {
  return getApiErrorMessages(error).join("\n");
}

export function getStaleEntityConflict<TActivity = unknown>(error: unknown) {
  const appError = normalizeAppError(error);
  const data = appError.problem;
  if (
    appError.status !== 409 ||
    !data ||
    data.type !== "urn:melody-track:problem:stale-entity" ||
    !hasString(data, "entityType") ||
    !hasString(data, "entityId")
  ) {
    return null;
  }

  return data as StaleEntityConflict<TActivity>;
}

async function sendRequest<T>(method: HttpMethod, path: string, body?: unknown, config?: HttpRequestConfig): Promise<HttpResponse<T>> {
  const requestInformation = new RequestInformation(method);
  requestInformation.URL = buildRequestUrl(path, config?.params);
  requestInformation.headers.add("Accept", config?.responseType === "blob" ? "*/*" : "application/json");
  requestInformation.headers.addAllRaw(config?.headers ?? {});

  if (body !== undefined) {
    requestInformation.headers.add("Content-Type", "application/json");
    requestInformation.content = new TextEncoder().encode(JSON.stringify(body)).buffer;
  }

  requestInformation.addRequestOptions([
    new MelodyTrackRequestOption(config?.signal, config?.skipAuthRefresh),
    createResponseHandlerOption(config),
  ]);

  try {
    return (await requestAdapter.sendPrimitive(requestInformation, "string", undefined)) as unknown as HttpResponse<T>;
  } catch (error) {
    throw normalizeAppError(error);
  }
}

function createResponseHandlerOption(config?: HttpRequestConfig) {
  const option = new ResponseHandlerOption();
  option.responseHandler = {
    handleResponse: (async (response: unknown) => {
      const fetchResponse = response as Response;
      const acceptsStatus = config?.validateStatus?.(fetchResponse.status) ?? fetchResponse.ok;
      if (!acceptsStatus) {
        throw await createHttpError(fetchResponse);
      }

      let data: unknown;
      if (config?.responseType === "blob") {
        data = await fetchResponse.blob();
      } else if (fetchResponse.status === 204 || fetchResponse.headers.get("content-length") === "0") {
        data = undefined;
      } else {
        const contentType = fetchResponse.headers.get("content-type")?.toLowerCase() ?? "";
        data = contentType.includes("json") ? await fetchResponse.json() : await fetchResponse.text();
      }

      return { data, headers: fetchResponse.headers, status: fetchResponse.status };
    }) as ResponseHandler["handleResponse"],
  };
  return option;
}

async function createHttpError(response: Response) {
  const problem = await readProblemDetails(response);
  const status = response.status;
  const message =
    problem?.detail ??
    problem?.title ??
    (status === 401 ? "Сессия истекла. Войдите снова." : `Сервер не смог обработать запрос (HTTP ${String(status)}).`);
  return new AppError(message, {
    kind: status === 401 ? "authentication" : "http",
    status,
    problem: problem ?? undefined,
  });
}

async function readProblemDetails(response: Response) {
  const contentType = response.headers.get("content-type")?.toLowerCase() ?? "";
  if (!contentType.includes("json")) {
    return null;
  }

  try {
    const value: unknown = await response.clone().json();
    return isApiProblemDetails(value) ? value : null;
  } catch {
    return null;
  }
}

function getSharedRefreshRequest() {
  refreshRequest ??= refreshAccessToken().finally(() => {
    refreshRequest = null;
  });
  return refreshRequest;
}

async function refreshAccessToken() {
  const lockManager = typeof navigator === "undefined" ? undefined : (Reflect.get(navigator, "locks") as LockManager | undefined);
  if (lockManager) {
    const requestedAt = Date.now();
    return lockManager.request(refreshChannelName, () => {
      if (lastSharedAccessToken && lastSharedAccessToken.receivedAt >= requestedAt) {
        return lastSharedAccessToken.accessToken;
      }

      return refreshAccessTokenUnlocked();
    });
  }

  return refreshAccessTokenUnlocked();
}

async function refreshAccessTokenUnlocked() {
  let csrfToken = readCookie(csrfCookieName);

  for (let attempt = 0; attempt < 2; attempt += 1) {
    try {
      const response = await http.post<{ accessToken: string }>("/auth/refresh", undefined, { skipAuthRefresh: true });
      httpSession?.setAccessToken(response.data.accessToken);
      lastSharedAccessToken = { accessToken: response.data.accessToken, receivedAt: Date.now() };
      refreshChannel?.postMessage({ type: "refreshed", accessToken: response.data.accessToken });
      authExpiryPublished = false;
      return response.data.accessToken;
    } catch (error) {
      const appError = normalizeAppError(error);
      const currentCsrfToken = readCookie(csrfCookieName);
      const cookieWasRotated = currentCsrfToken !== csrfToken;
      if (attempt === 0 && (appError.status === 401 || appError.status === 403) && cookieWasRotated) {
        csrfToken = currentCsrfToken;
        continue;
      }
      if (attempt === 0 && (appError.kind === "network" || (appError.status !== undefined && appError.status >= 500))) {
        continue;
      }
      if (appError.status === 401 || appError.status === 403) {
        return null;
      }
      throw new AppError("Не удалось обновить сессию. Повторите попытку.", {
        kind: appError.kind,
        status: appError.status,
        problem: appError.problem,
        cause: appError,
      });
    }
  }

  return null;
}

function withBrowserRequestOptions(url: string, requestInit: RequestInit, option?: MelodyTrackRequestOption): RequestInit {
  const headers = new Headers(requestInit.headers);
  if (isCookieAuthenticatedUrl(url)) {
    const csrfToken = readCookie(csrfCookieName);
    if (csrfToken) {
      headers.set(csrfHeaderName, csrfToken);
    }
  }

  return {
    ...requestInit,
    credentials: "include",
    headers,
    signal: option?.signal,
  };
}

function publishAuthExpiry() {
  if (authExpiryPublished) {
    return;
  }
  authExpiryPublished = true;
  httpSession?.clear();
  window.dispatchEvent(new Event(authExpiredEventName));
}

function buildRequestUrl(path: string, params?: object) {
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  const base = new URL(`${apiBaseUrl}${normalizedPath}`, window.location.origin);
  for (const [key, value] of Object.entries(params ?? {})) {
    if (value !== undefined && value !== null) {
      base.searchParams.set(key, value instanceof Date ? value.toISOString() : String(value));
    }
  }
  return base.toString();
}

function resolveAdapterBaseUrl() {
  const url = new URL(apiBaseUrl || "/api", window.location.origin);
  url.pathname = url.pathname.replace(/\/api\/?$/, "") || "/";
  return url.toString().replace(/\/$/, "");
}

function isPublicAuthUrl(url: string) {
  return publicAuthPaths.some((path) => new URL(url).pathname.endsWith(path));
}

function isRefreshUrl(url: string) {
  return new URL(url).pathname.endsWith("/auth/refresh");
}

function isCookieAuthenticatedUrl(url: string) {
  const pathname = new URL(url).pathname;
  return pathname.endsWith("/auth/refresh") || pathname.endsWith("/auth/logout");
}

function readCookie(name: string) {
  if (typeof document === "undefined") {
    return null;
  }

  const prefix = `${encodeURIComponent(name)}=`;
  for (const cookie of document.cookie.split(";")) {
    const value = cookie.trim();
    if (value.startsWith(prefix)) {
      return decodeURIComponent(value.slice(prefix.length));
    }
  }
  return null;
}

function isApiProblemDetails(value: unknown): value is ApiProblemDetails {
  if (!value || typeof value !== "object") {
    return false;
  }

  return (
    hasString(value, "type") &&
    hasString(value, "title") &&
    hasNumber(value, "status") &&
    hasString(value, "instance") &&
    hasString(value, "code") &&
    hasString(value, "traceId") &&
    "errors" in value &&
    Array.isArray(value.errors) &&
    value.errors.every(isApiValidationError)
  );
}

function isApiValidationError(value: unknown): value is ApiValidationError {
  return Boolean(value && typeof value === "object" && hasString(value, "path") && hasString(value, "code") && hasString(value, "message"));
}

function isSharedRefreshMessage(value: unknown): value is { type: "refreshed"; accessToken: string } {
  return Boolean(value && typeof value === "object" && Reflect.get(value, "type") === "refreshed" && hasString(value, "accessToken"));
}

function hasString(value: object, key: string) {
  return key in value && typeof value[key as keyof typeof value] === "string";
}

function hasNumber(value: object, key: string) {
  return key in value && typeof value[key as keyof typeof value] === "number";
}
