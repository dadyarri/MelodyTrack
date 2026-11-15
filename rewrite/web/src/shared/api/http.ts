import type { RequestOption } from "@microsoft/kiota-abstractions";
import {
  FetchRequestAdapter,
  HttpClient,
  type Middleware,
} from "@microsoft/kiota-http-fetchlibrary";
import { createRewriteClient } from "./generated/rewriteClient";

export class ApiError extends Error {
  readonly status: number;
  readonly traceId: string | undefined;
  readonly retryAfterSeconds: number | undefined;
  readonly transient: boolean;

  constructor(
    message: string,
    status = 0,
    traceId?: string,
    retryAfterSeconds?: number,
    transient = status === 0 || status === 429 || (status >= 500 && status <= 599),
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.traceId = traceId;
    this.retryAfterSeconds = retryAfterSeconds;
    this.transient = transient;
  }
}

export function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === "AbortError";
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error;
  if (error instanceof SyntaxError) return invalidResponse();
  return new ApiError("Нет соединения с сервером. Повторите попытку.");
}

export function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** The location must be a static schema path, never a response value or record identifier. */
export function invalidResponse(location?: string): ApiError {
  return new ApiError(
    location && import.meta.env.DEV
      ? `Не удалось прочитать ответ сервера. Поле: ${location}.`
      : "Не удалось прочитать ответ сервера.",
    502,
    undefined,
    undefined,
    false,
  );
}

class BrowserRequestOption implements RequestOption {
  static readonly key = "MelodyTrackBrowserRequest";

  constructor(
    readonly signal: AbortSignal,
    readonly unauthorizedMessage?: string,
  ) {}

  getKey() {
    return BrowserRequestOption.key;
  }
}

export function apiOptions(signal: AbortSignal, unauthorizedMessage?: string) {
  return { options: [new BrowserRequestOption(signal, unauthorizedMessage)] };
}

class BrowserMiddleware implements Middleware {
  next: Middleware | undefined;

  async execute(url: string, init: RequestInit, options?: Record<string, RequestOption>) {
    const option = options?.[BrowserRequestOption.key] as BrowserRequestOption | undefined;
    if (!this.next || !option) throw new ApiError("Не удалось подготовить запрос.");
    let response: Response;
    try {
      response = await this.next.execute(
        url,
        {
          ...init,
          headers: new Headers(init.headers),
          credentials: "include",
          cache: "no-store",
          redirect: "error",
          signal: option.signal,
        },
        options,
      );
    } catch (error) {
      if (isAbortError(error)) throw error;
      throw toApiError(error);
    }
    if (response.ok) return response;
    throw await responseError(response, option.unauthorizedMessage);
  }
}

export function applicationApi(accessToken?: string) {
  const adapter = new FetchRequestAdapter(
    {
      authenticateRequest(request) {
        if (accessToken) request.headers.add("Authorization", `Bearer ${accessToken}`);
        return Promise.resolve();
      },
    },
    undefined,
    undefined,
    new HttpClient((url, init) => fetch(url, init), new BrowserMiddleware()),
  );
  adapter.baseUrl = window.location.origin;
  return createRewriteClient(adapter).api;
}

export function staffApi(accessToken?: string) {
  return applicationApi(accessToken).auth;
}

async function responseError(response: Response, unauthorizedMessage?: string): Promise<ApiError> {
  let payload: unknown;
  try {
    payload = await response.json();
  } catch (error) {
    if (isAbortError(error)) throw error;
  }
  const possibleTraceId = isObject(payload) ? payload.traceId : undefined;
  const traceId =
    typeof possibleTraceId === "string" && /^[a-fA-F0-9-]{16,64}$/.test(possibleTraceId)
      ? possibleTraceId
      : undefined;
  const retryAfter = Number(response.headers.get("Retry-After"));
  const retryAfterSeconds =
    response.status === 429 && Number.isFinite(retryAfter) && retryAfter > 0
      ? Math.min(Math.ceil(retryAfter), 3600)
      : undefined;
  const message =
    response.status === 401
      ? (unauthorizedMessage ?? "Сессия завершена. Войдите снова.")
      : response.status === 403
        ? "Нет доступа."
        : response.status === 429
          ? "Слишком много попыток. Повторите позже."
          : response.status === 409
            ? "Состояние изменилось. Повторите действие."
            : response.status === 400
              ? "Проверьте введённые данные."
              : "Сервис недоступен. Повторите позже.";
  // Raw Problem Details never become diagnostic text or UI messages.
  return new ApiError(message, response.status, traceId, retryAfterSeconds);
}
