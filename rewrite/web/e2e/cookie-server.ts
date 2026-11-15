import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";

const appOrigin = "http://localhost:5174";
const staffId = "019a2c63-8c00-7000-8000-000000000001";
const cookieName = "synthetic-session";
const cookiePath = "/api/auth";
const cookieMaxAgeSeconds = 1_209_600;

const identity = {
  staffId,
  login: "browser.synthetic",
  role: "teacher",
  permissions: [
    { permission: "clientsRead", allowed: true, dataScope: "available", timeScope: "any" },
  ],
};

type Credentials = {
  staffId: string;
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
};

function readCookie(request: IncomingMessage, name: string): string | null {
  const header = request.headers.cookie;
  if (!header) return null;
  for (const segment of header.split(";")) {
    const separator = segment.indexOf("=");
    if (separator < 0 || segment.slice(0, separator).trim() !== name) continue;
    return segment.slice(separator + 1).trim();
  }
  return null;
}

function bearerToken(request: IncomingMessage): string | null {
  const authorization = request.headers.authorization;
  const match = authorization?.match(/^Bearer ([^\s]+)$/);
  return match?.[1] ?? null;
}

function corsHeaders(request: IncomingMessage): Record<string, string> {
  if (request.headers.origin !== appOrigin) return {};
  return {
    "Access-Control-Allow-Origin": appOrigin,
    "Access-Control-Allow-Credentials": "true",
    "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
    "Access-Control-Allow-Headers": "Authorization, Content-Type",
    "Access-Control-Max-Age": "0",
    Vary: "Origin",
  };
}

function sendJson(
  request: IncomingMessage,
  response: ServerResponse,
  status: number,
  body: unknown,
  extraHeaders: Record<string, string> = {},
): void {
  response.writeHead(status, {
    ...corsHeaders(request),
    "Cache-Control": "no-store",
    "Content-Type": "application/json; charset=utf-8",
    ...extraHeaders,
  });
  response.end(JSON.stringify(body));
}

function sendNoContent(
  request: IncomingMessage,
  response: ServerResponse,
  extraHeaders: Record<string, string> = {},
): void {
  response.writeHead(204, {
    ...corsHeaders(request),
    "Cache-Control": "no-store",
    ...extraHeaders,
  });
  response.end();
}

function setSessionCookie(value: string): string {
  return `${cookieName}=${value}; HttpOnly; SameSite=Strict; Path=${cookiePath}; Max-Age=${cookieMaxAgeSeconds}`;
}

function clearSessionCookie(): string {
  return `${cookieName}=; HttpOnly; SameSite=Strict; Path=${cookiePath}; Max-Age=0`;
}

export async function startCookieServer(): Promise<{
  origin: string;
  maximumRestores(): number;
  waitForEventStream(): Promise<void>;
  invalidate(): void;
  close(): Promise<void>;
}> {
  let credentialGeneration = 0;
  let activeRestores = 0;
  let maximumConcurrentRestores = 0;
  let currentCookie: string | null = null;
  let currentAccessToken: string | null = null;
  const accessTokens = new Set<string>();
  const eventStreams = new Map<ServerResponse, ReturnType<typeof setInterval>>();
  const eventStreamWaiters = new Set<() => void>();

  const waitForEventStream = () =>
    eventStreams.size > 0
      ? Promise.resolve()
      : new Promise<void>((resolve) => eventStreamWaiters.add(resolve));

  const closeEventStreams = () => {
    for (const [response, heartbeat] of eventStreams) {
      clearInterval(heartbeat);
      response.end();
    }
    eventStreams.clear();
  };

  const invalidate = () => {
    for (const response of eventStreams.keys()) {
      if (!response.destroyed) response.write("event: invalidate\ndata: {}\n\n");
    }
  };

  const issueCredentials = (): Credentials => {
    credentialGeneration += 1;
    currentCookie = `session-${credentialGeneration}`;
    currentAccessToken = `access-${credentialGeneration}`;
    accessTokens.add(currentAccessToken);
    return {
      staffId,
      accessToken: currentAccessToken,
      refreshToken: `refresh-${credentialGeneration}`,
      expiresIn: 300,
    };
  };

  const server: Server = createServer((request, response) => {
    void (async () => {
      const requestOrigin = request.headers.origin;
      if (request.method === "OPTIONS") {
        if (requestOrigin !== appOrigin) {
          sendJson(request, response, 403, {});
          return;
        }
        sendNoContent(request, response);
        return;
      }

      if (request.method === "POST") {
        for await (const chunk of request) void chunk;
      }

      const path = new URL(request.url ?? "/", "http://localhost").pathname;
      if (request.method === "GET" && path === "/api/changes/events") {
        if (!accessTokens.has(bearerToken(request) ?? "")) {
          sendJson(request, response, 401, {});
          return;
        }

        response.writeHead(200, {
          ...corsHeaders(request),
          "Cache-Control": "no-cache, no-transform",
          Connection: "keep-alive",
          "Content-Type": "text/event-stream; charset=utf-8",
        });
        response.flushHeaders();
        const heartbeat = setInterval(() => {
          if (!response.destroyed) response.write(": keepalive\n\n");
        }, 15_000);
        eventStreams.set(response, heartbeat);
        for (const resolve of eventStreamWaiters) resolve();
        eventStreamWaiters.clear();
        response.once("close", () => {
          clearInterval(heartbeat);
          eventStreams.delete(response);
        });
        response.write("event: invalidate\ndata: {}\n\n");
        return;
      }

      if (request.method === "POST" && path === "/api/auth/staff/password") {
        const credentials = issueCredentials();
        sendJson(request, response, 200, credentials, {
          "Set-Cookie": setSessionCookie(currentCookie as string),
        });
        return;
      }

      if (request.method === "POST" && path === "/api/auth/staff/restore") {
        if (!currentCookie || readCookie(request, cookieName) !== currentCookie) {
          sendJson(request, response, 401, {});
          return;
        }

        activeRestores += 1;
        maximumConcurrentRestores = Math.max(maximumConcurrentRestores, activeRestores);
        try {
          await new Promise<void>((resolve) => setTimeout(resolve, 80));
          const credentials = issueCredentials();
          sendJson(request, response, 200, credentials, {
            "Set-Cookie": setSessionCookie(currentCookie as string),
          });
        } finally {
          activeRestores -= 1;
        }
        return;
      }

      if (request.method === "GET" && path === "/api/auth/me") {
        if (!accessTokens.has(bearerToken(request) ?? "")) {
          sendJson(request, response, 401, {});
          return;
        }
        sendJson(request, response, 200, identity);
        return;
      }

      if (request.method === "POST" && path === "/api/auth/logout") {
        if (!accessTokens.has(bearerToken(request) ?? "")) {
          sendJson(request, response, 401, {});
          return;
        }
        currentCookie = null;
        currentAccessToken = null;
        accessTokens.clear();
        closeEventStreams();
        sendNoContent(request, response, { "Set-Cookie": clearSessionCookie() });
        return;
      }

      sendJson(request, response, 404, {});
    })().catch(() => {
      if (!response.headersSent) sendJson(request, response, 500, {});
      else response.destroy();
    });
  });

  await new Promise<void>((resolve, reject) => {
    const onError = (error: Error) => reject(error);
    server.once("error", onError);
    server.listen(0, "localhost", () => {
      server.off("error", onError);
      resolve();
    });
  });

  const address = server.address();
  if (!address || typeof address === "string") {
    await new Promise<void>((resolve) => server.close(() => resolve()));
    throw new Error("Cookie test server did not bind to a TCP port.");
  }
  const { port } = address;

  return {
    origin: `http://localhost:${port}`,
    maximumRestores: () => maximumConcurrentRestores,
    waitForEventStream,
    invalidate,
    close: () =>
      new Promise<void>((resolve, reject) => {
        closeEventStreams();
        server.close((error) => (error ? reject(error) : resolve()));
      }),
  };
}
