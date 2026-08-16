import { useCallback } from "react";
import { useSearchParams } from "react-router";

export type UrlStateValue = string | number | null | undefined;

export function readPositiveInteger(value: string | null, fallback = 1) {
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : fallback;
}

export function updateUrlSearchParams(current: URLSearchParams, updates: Record<string, UrlStateValue>) {
  const next = new URLSearchParams(current);
  for (const [key, value] of Object.entries(updates)) {
    if (value === null || value === undefined || value === "") {
      next.delete(key);
    } else {
      next.set(key, String(value));
    }
  }
  return next;
}

export function useUrlState() {
  const [searchParams, setSearchParams] = useSearchParams();
  const setUrlState = useCallback(
    (updates: Record<string, UrlStateValue>, options?: { replace?: boolean }) => {
      setSearchParams((current) => updateUrlSearchParams(current, updates), options);
    },
    [setSearchParams],
  );

  return { searchParams, setUrlState };
}
