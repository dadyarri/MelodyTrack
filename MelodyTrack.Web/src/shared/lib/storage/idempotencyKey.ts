export function createIdempotencyKey() {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }

  return `${String(Date.now())}-${Math.random().toString(16).slice(2)}`;
}
