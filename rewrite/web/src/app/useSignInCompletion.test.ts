import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useSignInCompletion } from "./useSignInCompletion";

type Entry = {
  staffId: string | null;
  status: Parameters<typeof useSignInCompletion>[1];
};

function renderEntry(status: Entry["status"] = "anonymous") {
  return renderHook(({ staffId, status }: Entry) => useSignInCompletion(staffId, status), {
    initialProps: { staffId: null, status } as Entry,
  });
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal(
    "matchMedia",
    vi.fn(() => ({ matches: true })),
  );
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("sign-in completion", () => {
  it("keeps a successful interactive sign-in visible long enough for its final gesture", () => {
    const { result, rerender } = renderEntry();
    rerender({ staffId: null, status: "authenticating" });
    rerender({ staffId: "staff-demo", status: "authenticated" });

    expect(result.current).toBe(true);

    act(() => vi.advanceTimersByTime(649));

    expect(result.current).toBe(true);

    act(() => vi.advanceTimersByTime(1));

    expect(result.current).toBe(false);
  });

  it("also finishes a sign-in whose request resolves before an authenticating render", () => {
    const { result, rerender } = renderEntry();

    rerender({ staffId: "staff-demo", status: "authenticated" });

    expect(result.current).toBe(true);
  });

  it("opens a restored session immediately", () => {
    const { result, rerender } = renderEntry("restoring");

    rerender({ staffId: "staff-demo", status: "authenticated" });

    expect(result.current).toBe(false);
  });

  it("opens immediately when the mascot is hidden or motion is reduced", () => {
    vi.stubGlobal(
      "matchMedia",
      vi.fn(() => ({ matches: false })),
    );
    const { result, rerender } = renderEntry();

    rerender({ staffId: "staff-demo", status: "authenticated" });

    expect(result.current).toBe(false);
  });

  it("cancels completion if the session is cleared", () => {
    const { result, rerender } = renderEntry();
    rerender({ staffId: "staff-demo", status: "authenticated" });

    rerender({ staffId: null, status: "anonymous" });
    act(() => vi.advanceTimersByTime(650));

    expect(result.current).toBe(false);
  });
});
