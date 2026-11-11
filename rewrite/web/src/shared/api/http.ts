export class ApiError extends Error {
  readonly status: number;
  readonly traceId: string | undefined;
  readonly retryAfterSeconds: number | undefined;

  constructor(message: string, status = 0, traceId?: string, retryAfterSeconds?: number) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.traceId = traceId;
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

export function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === "AbortError";
}

export function toApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError("Не удалось связаться с сервером. Проверьте подключение и повторите действие.");
}

export function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

export function invalidResponse(): ApiError {
  return new ApiError(
    "Сервер вернул неожиданный ответ. Повторите действие или обратитесь за помощью.",
    502,
  );
}

type RequestOptions = {
  method?: "GET" | "POST";
  body?: unknown;
  accessToken?: string;
  signal: AbortSignal;
  unauthorizedMessage?: string;
};

export async function requestJson(path: string, options: RequestOptions): Promise<unknown> {
  if (!path.startsWith("/api/")) throw new ApiError("Недопустимый адрес запроса.");
  const headers = new Headers({ Accept: "application/json" });
  if (options.body !== undefined) headers.set("Content-Type", "application/json");
  if (options.accessToken) headers.set("Authorization", `Bearer ${options.accessToken}`);

  let response: Response;
  try {
    response = await fetch(path, {
      method: options.method ?? "GET",
      headers,
      credentials: "include",
      cache: "no-store",
      redirect: "error",
      signal: options.signal,
      ...(options.body !== undefined ? { body: JSON.stringify(options.body) } : {}),
    });
  } catch (error) {
    if (isAbortError(error)) throw error;
    throw toApiError(error);
  }

  if (response.status === 204 && response.ok) return null;
  let payload: unknown;
  try {
    payload = await response.json();
  } catch (error) {
    if (isAbortError(error)) throw error;
    if (response.ok) throw invalidResponse();
  }
  if (response.ok) return payload;

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
      ? (options.unauthorizedMessage ??
        "Сессия завершилась или активный аккаунт изменился. Войдите снова.")
      : response.status === 403
        ? "Нет доступа к этому действию. Обратитесь к администратору."
        : response.status === 429
          ? "Слишком много попыток. Подождите и повторите вход."
          : response.status === 409
            ? "Состояние изменилось. Начните действие заново."
            : response.status === 400
              ? "Не удалось выполнить действие. Проверьте введённые данные."
              : "Сервис временно недоступен. Повторите действие позже.";
  // Raw Problem Details and credential response bodies never become diagnostic text.
  throw new ApiError(message, response.status, traceId, retryAfterSeconds);
}
