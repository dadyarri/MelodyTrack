import { describe, expect, it, vi } from "vitest";

import { logoutSession } from "./logoutSession";

describe("logoutSession", () => {
  it("revokes the cookie-backed session and always clears local state", async () => {
    const revoke = vi.fn().mockRejectedValue(new Error("backend unavailable"));
    const clear = vi.fn();

    await expect(
      logoutSession({
        revoke,
        clear,
      }),
    ).rejects.toThrow("backend unavailable");

    expect(revoke).toHaveBeenCalledOnce();
    expect(clear).toHaveBeenCalledOnce();
  });

  it("clears local state after a successful revocation", async () => {
    const revoke = vi.fn();
    const clear = vi.fn();

    await logoutSession({
      revoke,
      clear,
    });

    expect(revoke).toHaveBeenCalledOnce();
    expect(clear).toHaveBeenCalledOnce();
  });
});
