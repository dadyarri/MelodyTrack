import type { QueryClient } from "@tanstack/react-query";

const affectedQueries = new Set([
  "vacations",
  "vacation-requests",
  "working-hours-requests",
  "users",
  "clients",
  "schedule",
  "dashboard",
  "reports",
  "tasks",
  "client-portal",
  "portal",
  "analytics",
  "notifications",
  "audit-log",
]);
export function invalidateScheduling(queryClient: QueryClient) {
  return queryClient.invalidateQueries({ predicate: (query) => affectedQueries.has(String(query.queryKey[0])) });
}
