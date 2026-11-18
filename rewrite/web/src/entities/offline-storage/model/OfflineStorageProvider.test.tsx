import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { OfflineStorageProvider, useOfflineStorage } from "./OfflineStorageProvider";

const mocks = vi.hoisted(() => ({
  create: vi.fn(),
  unlock: vi.fn(),
  exists: vi.fn(),
  blocked: vi.fn(),
  mark: vi.fn(),
  markDeletion: vi.fn(),
  needsDeletion: vi.fn(),
  remove: vi.fn(),
  vault: { lock: vi.fn(), get: vi.fn(), put: vi.fn(), delete: vi.fn(), prune: vi.fn() },
}));
vi.mock("@/shared/offline", () => ({
  createOfflineVault: mocks.create,
  unlockOfflineVault: mocks.unlock,
  hasOfflineVault: mocks.exists,
  isOfflineVaultBlocked: mocks.blocked,
  setOfflineVaultBlocked: mocks.mark,
  markOfflineVaultForDeletion: mocks.markDeletion,
  needsOfflineVaultDeletion: mocks.needsDeletion,
  deleteOfflineVault: mocks.remove,
  OfflineVaultAlreadyExistsError: class extends Error {},
}));
const staffId = "019a2c63-8c00-7000-8000-000000000061";
const wrapper = ({ children }: { children: ReactNode }) => (
  <OfflineStorageProvider>{children}</OfflineStorageProvider>
);
beforeEach(() => {
  vi.resetAllMocks();
  localStorage.clear();
  mocks.exists.mockResolvedValue(true);
  mocks.blocked.mockResolvedValue(false);
  mocks.mark.mockResolvedValue(undefined);
  mocks.markDeletion.mockResolvedValue(undefined);
  mocks.needsDeletion.mockResolvedValue(false);
  mocks.remove.mockResolvedValue(undefined);
  mocks.create.mockResolvedValue(mocks.vault);
  mocks.unlock.mockResolvedValue(mocks.vault);
  mocks.vault.prune.mockResolvedValue(0);
  mocks.vault.get.mockResolvedValue({ value: { draft: true } });
});
afterEach(() => {
  cleanup();
  localStorage.clear();
  vi.restoreAllMocks();
});

describe("account offline storage", () => {
  it("removes a persistently revoked vault before reopening after a restart", async () => {
    mocks.needsDeletion.mockResolvedValue(true);
    mocks.remove.mockRejectedValueOnce(new Error("storage unavailable"));
    const hook = renderHook(useOfflineStorage, { wrapper });

    act(() =>
      hook.result.current?.authenticate(staffId, {
        source: "credential",
      }),
    );
    await waitFor(() => expect(hook.result.current?.status).toBe("error"));
    expect(mocks.unlock).not.toHaveBeenCalled();
    expect(mocks.mark).not.toHaveBeenCalled();

    act(() =>
      hook.result.current?.authenticate(staffId, {
        source: "credential",
      }),
    );
    await waitFor(() => expect(hook.result.current?.status).toBe("unlocked"));
    expect(mocks.remove).toHaveBeenCalledTimes(2);
  });
  it("retries a failed access revocation before opening the account again", async () => {
    const hook = renderHook(useOfflineStorage, { wrapper });
    act(() =>
      hook.result.current?.authenticate(staffId, {
        source: "credential",
      }),
    );
    await waitFor(() => expect(hook.result.current?.status).toBe("unlocked"));
    mocks.remove.mockRejectedValueOnce(new Error("storage unavailable"));

    await act(async () => hook.result.current?.revokeClientAccess());
    expect(hook.result.current?.status).toBe("error");
    mocks.remove.mockRejectedValueOnce(new Error("still unavailable"));
    act(() =>
      hook.result.current?.authenticate(staffId, {
        source: "credential",
      }),
    );
    await waitFor(() => expect(mocks.remove).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(hook.result.current?.status).toBe("error"));
    expect(mocks.unlock).toHaveBeenCalledTimes(1);

    act(() => {
      window.dispatchEvent(new Event("online"));
    });
    await waitFor(() => expect(mocks.remove).toHaveBeenCalledTimes(3));
    act(() =>
      hook.result.current?.authenticate(staffId, {
        source: "credential",
      }),
    );
    await waitFor(() => expect(hook.result.current?.status).toBe("unlocked"));
    expect(mocks.unlock).toHaveBeenCalledTimes(2);
  });
  it("automatically opens the account vault after credential sign-in", async () => {
    const hook = renderHook(useOfflineStorage, { wrapper });

    act(() =>
      hook.result.current?.authenticate(staffId, {
        source: "credential",
      }),
    );

    await waitFor(() => expect(hook.result.current?.status).toBe("unlocked"));
    expect(mocks.unlock).toHaveBeenCalledWith({ staffId });
    expect(mocks.mark).toHaveBeenCalledWith(staffId, false);
  });
  it("does not reopen a logged out vault through a restored cookie", async () => {
    mocks.blocked.mockResolvedValue(true);
    const hook = renderHook(useOfflineStorage, { wrapper });

    act(() => hook.result.current?.authenticate(staffId, { source: "restore" }));
    await waitFor(() => expect(hook.result.current?.status).toBe("blocked"));
    expect(hook.result.current?.status).toBe("blocked");
    expect(mocks.unlock).not.toHaveBeenCalled();
    expect(mocks.create).not.toHaveBeenCalled();
    expect(mocks.mark).not.toHaveBeenCalled();
  });
  it("persists logout through IndexedDB when localStorage refuses the marker", async () => {
    const hook = renderHook(useOfflineStorage, { wrapper });
    act(() =>
      hook.result.current?.authenticate(staffId, {
        source: "credential",
      }),
    );
    await waitFor(() => expect(hook.result.current?.status).toBe("unlocked"));
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("unavailable");
    });

    await act(async () => hook.result.current?.logout());

    expect(hook.result.current?.status).toBe("blocked");
    expect(mocks.mark).toHaveBeenLastCalledWith(staffId, true);
    expect(mocks.vault.lock).toHaveBeenCalled();
  });
  it("closes an open vault if the access marker cannot be read", async () => {
    const hook = renderHook(useOfflineStorage, { wrapper });
    act(() =>
      hook.result.current?.authenticate(staffId, {
        source: "credential",
      }),
    );
    await waitFor(() => expect(hook.result.current?.status).toBe("unlocked"));
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("unavailable");
    });

    await act(async () => {
      await expect(hook.result.current?.get("draft")).rejects.toThrow();
    });

    expect(hook.result.current?.status).toBe("blocked");
    expect(mocks.vault.get).not.toHaveBeenCalled();
  });
  it("discards an unlock that finishes after logout", async () => {
    let finish: (value: typeof mocks.vault) => void = () => {};
    mocks.unlock.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    const hook = renderHook(useOfflineStorage, { wrapper });
    act(() =>
      hook.result.current?.authenticate(staffId, {
        source: "credential",
      }),
    );
    await waitFor(() => expect(mocks.unlock).toHaveBeenCalled());

    await act(async () => {
      await hook.result.current?.logout();
      finish(mocks.vault);
    });

    expect(hook.result.current?.status).toBe("blocked");
    expect(mocks.vault.lock).toHaveBeenCalled();
    expect(mocks.vault.prune).not.toHaveBeenCalled();
  });
  it("rejects storage callbacks retained by the previous account", async () => {
    const hook = renderHook(useOfflineStorage, { wrapper });
    act(() =>
      hook.result.current?.authenticate(staffId, {
        source: "credential",
      }),
    );
    await waitFor(() => expect(hook.result.current?.status).toBe("unlocked"));
    const previous = hook.result.current;

    act(() =>
      hook.result.current?.authenticate("019a2c63-8c00-7000-8000-000000000062", {
        source: "credential",
      }),
    );
    await waitFor(() => expect(hook.result.current?.status).toBe("unlocked"));

    await expect(previous?.get("draft")).rejects.toThrow();
    await expect(previous?.put("draft", {}, "pending")).rejects.toThrow();
    await expect(previous?.remove("draft")).rejects.toThrow();
    expect(mocks.vault.get).not.toHaveBeenCalled();
    expect(mocks.vault.put).not.toHaveBeenCalled();
    expect(mocks.vault.delete).not.toHaveBeenCalled();
  });
});
