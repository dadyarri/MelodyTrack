import { useEffect, useState } from "react";
import type { useStaffSession } from "@/entities/session";

type SessionStatus = ReturnType<typeof useStaffSession>["status"];

// Keep only the presentation on screen; authentication itself is already complete.
export function useSignInCompletion(staffId: string | null, status: SessionStatus) {
  const [entry, setEntry] = useState({ staffId, status, finishing: false });

  if (entry.staffId !== staffId || entry.status !== status) {
    const interactiveSignIn =
      !entry.staffId && (entry.status === "anonymous" || entry.status === "authenticating");
    const showMotion = window.matchMedia(
      "(min-width: 1024px) and (prefers-reduced-motion: no-preference)",
    ).matches;
    setEntry({
      staffId,
      status,
      finishing:
        Boolean(staffId) &&
        ((entry.finishing && entry.staffId === staffId) || (interactiveSignIn && showMotion)),
    });
  }

  useEffect(() => {
    if (!entry.finishing) return;
    const timer = window.setTimeout(() => {
      setEntry((current) =>
        current.staffId === entry.staffId ? { ...current, finishing: false } : current,
      );
    }, 650);
    return () => window.clearTimeout(timer);
  }, [entry.finishing, entry.staffId]);

  return entry.finishing;
}
