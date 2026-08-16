import axios, { AxiosError, type InternalAxiosRequestConfig } from "axios";

import { apiBaseUrl } from "../config";

export type HttpSession = {
  clear: () => void;
  clearLegacyRefreshToken: () => void;
  getAccessToken: () => string | null;
  getLegacyRefreshToken: () => string | null;
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
};

export type StaleEntityConflict<TActivity = unknown> = ApiProblemDetails & {
  entityType: string;
  entityId: string;
  currentActivity?: TActivity | null;
};

export const authExpiredEventName = "melodytrack:auth-expired";

export const http = axios.create({
  baseURL: apiBaseUrl,
  withCredentials: true,
  headers: {
    "Content-Type": "application/json",
  },
});

let refreshRequest: Promise<string | null> | null = null;
let httpSession: HttpSession | null = null;
const legacyCacheStorageKeyPrefix = "melodytrack:http-cache:";
const csrfCookieName = "MelodyTrack.Csrf";
const csrfHeaderName = "X-CSRF-Token";

if (typeof window !== "undefined") {
  discardLegacyHttpCache(window.localStorage);
}

export function configureHttpSession(session: HttpSession) {
  httpSession = session;
}

export function isHttpRequestCanceled(error: unknown) {
  return axios.isCancel(error);
}

export function restoreAccessToken() {
  const accessToken = httpSession?.getAccessToken();
  if (accessToken) {
    return Promise.resolve(accessToken);
  }

  refreshRequest ??= refreshAccessToken().finally(() => {
    refreshRequest = null;
  });
  return refreshRequest;
}

http.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const accessToken = httpSession?.getAccessToken();
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`;
  }

  const csrfToken = readCookie(csrfCookieName);
  if (csrfToken && isStateChangingMethod(config.method)) {
    config.headers[csrfHeaderName] = csrfToken;
  }

  if (typeof navigator !== "undefined" && !navigator.onLine) {
    return Promise.reject(new AxiosError("Сеть недоступна", AxiosError.ERR_NETWORK, config));
  }

  return config;
});

http.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retry?: boolean }) | undefined;
    const publicAuthUrls = [
      "/client-portal/auth/link",
      "/auth/login",
      "/auth/register",
      "/auth/invites",
      "/auth/2fa/verify",
      "/auth/2fa/recover",
      "/auth/password-reset",
    ];
    const isPublicAuthRequest = Boolean(original?.url && publicAuthUrls.some((url) => original.url?.includes(url)));

    if (error.response?.status !== 401 || !original || original._retry || isPublicAuthRequest || original.url?.includes("/auth/refresh")) {
      return Promise.reject(error);
    }

    original._retry = true;
    refreshRequest ??= refreshAccessToken().finally(() => {
      refreshRequest = null;
    });

    const token = await refreshRequest;
    if (!token) {
      httpSession?.clear();
      window.dispatchEvent(new Event(authExpiredEventName));
      return Promise.reject(new Error("Сессия истекла. Войдите снова."));
    }

    original.headers.Authorization = `Bearer ${token}`;
    return http(original);
  },
);

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

  try {
    await axios.get(`${apiBaseUrl}/auth/me`, {
      headers: {
        Authorization: `Bearer ${accessToken}`,
      },
      timeout: 3000,
      validateStatus: () => true,
    });
    return true;
  } catch {
    return false;
  }
}

async function refreshAccessToken() {
  const legacyRefreshToken = httpSession?.getLegacyRefreshToken();
  let csrfToken = readCookie(csrfCookieName);

  for (let attempt = 0; attempt < 2; attempt += 1) {
    try {
      const response = await axios.post<{ accessToken: string }>(
        `${apiBaseUrl}/auth/refresh`,
        legacyRefreshToken ? { refreshToken: legacyRefreshToken } : {},
        {
          headers: {
            "Content-Type": "application/json",
            ...(csrfToken ? { [csrfHeaderName]: csrfToken } : {}),
          },
          withCredentials: true,
        },
      );
      httpSession?.setAccessToken(response.data.accessToken);
      httpSession?.clearLegacyRefreshToken();
      return response.data.accessToken;
    } catch (error) {
      const status = axios.isAxiosError(error) ? error.response?.status : undefined;
      const currentCsrfToken = readCookie(csrfCookieName);
      const cookieWasRotated = currentCsrfToken !== csrfToken;
      if (attempt === 0 && (status === 401 || status === 403) && cookieWasRotated) {
        csrfToken = currentCsrfToken;
        continue;
      }

      if (attempt === 0 && (status === undefined || status >= 500)) {
        continue;
      }

      if (status === 401) {
        return null;
      }

      throw new Error("Не удалось обновить сессию. Повторите попытку.", { cause: error });
    }
  }

  return null;
}

function isStateChangingMethod(method?: string) {
  return method !== undefined && ["post", "put", "patch", "delete"].includes(method.toLowerCase());
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

export function getApiProblemDetails(error: unknown) {
  if (!axios.isAxiosError(error) || !isApiProblemDetails(error.response?.data)) {
    return null;
  }

  return error.response.data;
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
  if (!axios.isAxiosError(error)) {
    return error instanceof Error ? [error.message] : ["Произошла неизвестная ошибка"];
  }

  if (!error.response) {
    return ["Не удалось подключиться к серверу. Проверьте соединение или попробуйте позже."];
  }

  const problem = getApiProblemDetails(error);
  if (!problem) {
    return [`Сервер не смог обработать запрос (HTTP ${String(error.response.status)}).`];
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
  if (!axios.isAxiosError(error) || error.response?.status !== 409) {
    return null;
  }

  const data: unknown = error.response.data;
  if (
    !isApiProblemDetails(data) ||
    data.type !== "urn:melody-track:problem:stale-entity" ||
    !hasString(data, "entityType") ||
    !hasString(data, "entityId")
  ) {
    return null;
  }

  return data as StaleEntityConflict<TActivity>;
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

function hasString(value: object, key: string) {
  return key in value && typeof value[key as keyof typeof value] === "string";
}

function hasNumber(value: object, key: string) {
  return key in value && typeof value[key as keyof typeof value] === "number";
}
