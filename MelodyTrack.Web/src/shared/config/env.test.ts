import { describe, expect, it } from "vitest";

import { resolveApiBaseUrl } from "./env";

describe("resolveApiBaseUrl", () => {
  it.each([
    ["https://api.example.com/", "https://api.example.com"],
    ["http://localhost:5000", "http://localhost:5000"],
    ["/api/", "/api"],
    ["/", ""],
  ])("normalizes %s", (input, expected) => {
    expect(resolveApiBaseUrl({ VITE_API_BASE_URL: input })).toBe(expected);
  });

  it.each([
    undefined,
    "",
    "api",
    "//example.com",
    "ftp://example.com",
    "/api?tenant=one",
    "https://user:secret@example.com",
  ])("rejects invalid value %s", (input) => {
    expect(() => resolveApiBaseUrl({ VITE_API_BASE_URL: input })).toThrow();
  });
});
