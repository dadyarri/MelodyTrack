import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { releaseNotesSeenKey } from "../model/releaseNotesStorage";
import { ReleaseNotesProvider, ReleaseVersion } from "./ReleaseNotes";

const api = vi.hoisted(() => ({ current: vi.fn(), history: vi.fn() }));
const dataChanges = vi.hoisted(() => ({ listeners: new Set<() => void>() }));
vi.mock("@/shared/api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/shared/api")>()),
  subscribeDataChanges: (listener: () => void) => {
    dataChanges.listeners.add(listener);
    return () => dataChanges.listeners.delete(listener);
  },
}));
vi.mock("@/entities/app-release", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/entities/app-release")>()),
  getCurrentRelease: api.current,
  getReleaseHistory: api.history,
}));

const current = { version: "2026.12.1", codename: "Da capo", codenameMeaning: "С начала, заново." };
const history = {
  currentVersion: current.version,
  releases: [
    {
      ...current,
      date: null,
      parentVersion: null,
      changes: { new: ["Полная переработка системы"], improved: [], fixed: [], security: [] },
    },
  ],
  page: 1,
  pageSize: 2,
  totalCount: 1,
  totalPages: 1,
  hasNextPage: false,
};

beforeEach(() => {
  dataChanges.listeners.clear();
  api.current.mockReset().mockResolvedValue(current);
  api.history.mockReset().mockResolvedValue(history);
  window.localStorage.clear();
  vi.spyOn(document, "visibilityState", "get").mockReturnValue("visible");
  vi.stubGlobal(
    "matchMedia",
    vi.fn(() => ({
      matches: false,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  );
});
afterEach(() => {
  cleanup();
  window.localStorage.clear();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("release notes", () => {
  it("waits until training closes and acknowledges the shown release for that account", async () => {
    const view = render(
      <ReleaseNotesProvider staffId="staff-a" canViewHistory enabled={false}>
        <ReleaseVersion />
      </ReleaseNotesProvider>,
    );
    await waitFor(() => expect(api.history).toHaveBeenCalledOnce());
    expect(screen.queryByRole("dialog")).toBeNull();

    view.rerender(
      <ReleaseNotesProvider staffId="staff-a" canViewHistory enabled>
        <ReleaseVersion />
      </ReleaseNotesProvider>,
    );
    await screen.findByText("Полная переработка системы");
    fireEvent.click(screen.getByRole("button", { name: "Понятно" }));

    await waitFor(() =>
      expect(window.localStorage.getItem(releaseNotesSeenKey("staff-a"))).toBe(current.version),
    );
    expect(window.localStorage.getItem(releaseNotesSeenKey("staff-b"))).toBeNull();
    expect(screen.queryByText("В разработке")).toBeNull();
  });

  it("lets staff without release-note access see only the backend version", async () => {
    render(
      <ReleaseNotesProvider staffId="staff-teacher" canViewHistory={false} enabled>
        <ReleaseVersion />
      </ReleaseNotesProvider>,
    );

    await screen.findByText("Версия 2026.12.1");

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.queryByRole("button", { name: /Список изменений/ })).toBeNull();
    expect(api.history).not.toHaveBeenCalled();
  });

  it("retries automatic history after reconnecting even when the current version is unchanged", async () => {
    api.history
      .mockRejectedValueOnce(new Error("synthetic network failure"))
      .mockResolvedValue(history);
    render(
      <ReleaseNotesProvider staffId="staff-a" canViewHistory enabled>
        <ReleaseVersion />
      </ReleaseNotesProvider>,
    );
    await waitFor(() => expect(api.history).toHaveBeenCalledOnce());
    expect(screen.queryByRole("dialog")).toBeNull();

    fireEvent.online(window);

    await screen.findByText("Полная переработка системы");
    expect(api.current).toHaveBeenCalledTimes(2);
    expect(api.history).toHaveBeenCalledTimes(2);
    expect(window.localStorage.getItem(releaseNotesSeenKey("staff-a"))).toBeNull();
  });

  it("keeps automatic notes visible while reconnect history is pending", async () => {
    let resolveHistory!: (value: typeof history) => void;
    const pendingHistory = new Promise<typeof history>((resolve) => {
      resolveHistory = resolve;
    });
    api.history.mockResolvedValueOnce(history).mockReturnValueOnce(pendingHistory);
    render(
      <ReleaseNotesProvider staffId="staff-a" canViewHistory enabled>
        <ReleaseVersion />
      </ReleaseNotesProvider>,
    );
    const existingChange = await screen.findByText("Полная переработка системы");
    const dialog = screen.getByRole("dialog");

    await act(async () => {
      for (const listener of dataChanges.listeners) listener();
    });
    await waitFor(() => expect(api.history).toHaveBeenCalledTimes(2));
    expect(screen.getByRole("dialog")).toBe(dialog);
    expect(screen.getByText("Полная переработка системы")).toBe(existingChange);
    expect(screen.queryByRole("progressbar")).toBeNull();

    const firstRelease = history.releases[0];
    if (!firstRelease) throw new Error("Expected release history fixture to have an entry");
    resolveHistory({
      ...history,
      releases: [
        { ...firstRelease, changes: { ...firstRelease.changes, new: ["Новое изменение"] } },
      ],
    });
    await screen.findByText("Новое изменение");
    expect(screen.getByRole("dialog")).toBe(dialog);
  });

  it("keeps the displayed release open while history for a newer version is pending", async () => {
    const nextCurrent = {
      version: "2026.12.2",
      codename: "Da capo II",
      codenameMeaning: "Снова с начала.",
    };
    const firstRelease = history.releases[0];
    if (!firstRelease) throw new Error("Expected release history fixture to have an entry");
    const nextHistory = {
      ...history,
      currentVersion: nextCurrent.version,
      releases: [
        {
          ...firstRelease,
          ...nextCurrent,
          changes: { ...firstRelease.changes, new: ["Изменения новой версии"] },
        },
      ],
    };
    let resolveHistory!: (value: typeof nextHistory) => void;
    const pendingHistory = new Promise<typeof nextHistory>((resolve) => {
      resolveHistory = resolve;
    });
    api.current.mockResolvedValueOnce(current).mockResolvedValue(nextCurrent);
    api.history.mockResolvedValueOnce(history).mockReturnValue(pendingHistory);
    render(
      <ReleaseNotesProvider staffId="staff-a" canViewHistory enabled>
        <ReleaseVersion />
      </ReleaseNotesProvider>,
    );
    const existingChange = await screen.findByText("Полная переработка системы");
    const dialog = screen.getByRole("dialog");

    await act(async () => {
      for (const listener of dataChanges.listeners) listener();
    });
    await screen.findByRole("button", { name: "Список изменений версии 2026.12.2" });
    await waitFor(() => expect(api.history.mock.calls.length).toBeGreaterThan(1));
    expect(screen.getByRole("dialog")).toBe(dialog);
    expect(screen.getByText("Полная переработка системы")).toBe(existingChange);
    expect(screen.queryByRole("progressbar")).toBeNull();

    resolveHistory(nextHistory);
    await screen.findByText("Изменения новой версии");
    expect(screen.getByRole("dialog")).toBe(dialog);
  });

  it("refreshes open release history without showing a loading state", async () => {
    window.localStorage.setItem(releaseNotesSeenKey("staff-a"), current.version);
    render(
      <ReleaseNotesProvider staffId="staff-a" canViewHistory enabled>
        <ReleaseVersion />
      </ReleaseNotesProvider>,
    );
    const versionButton = await screen.findByRole("button", {
      name: "Список изменений версии 2026.12.1",
    });
    fireEvent.click(versionButton);
    const acknowledgement = await screen.findByRole("button", { name: "Понятно" });
    acknowledgement.focus();
    expect(document.activeElement).toBe(acknowledgement);

    const firstRelease = history.releases[0];
    if (!firstRelease) throw new Error("Expected release history fixture to have an entry");
    api.history.mockResolvedValue({
      ...history,
      releases: [
        {
          ...firstRelease,
          changes: { ...firstRelease.changes, new: ["Свежее изменение"] },
        },
      ],
    });
    await act(async () => {
      for (const listener of dataChanges.listeners) listener();
    });

    await screen.findByText("Свежее изменение");
    expect(screen.queryByRole("progressbar")).toBeNull();
    expect(document.activeElement).toBe(acknowledgement);
  });

  it("retries a failed manual history without acknowledging unseen content on failure", async () => {
    window.localStorage.setItem(releaseNotesSeenKey("staff-a"), current.version);
    api.history
      .mockResolvedValueOnce(history)
      .mockRejectedValueOnce(new Error("synthetic network failure"))
      .mockResolvedValue(history);
    render(
      <ReleaseNotesProvider staffId="staff-a" canViewHistory enabled>
        <ReleaseVersion />
      </ReleaseNotesProvider>,
    );
    await waitFor(() => expect(api.history).toHaveBeenCalledOnce());
    const version = await screen.findByRole("button", {
      name: "Список изменений версии 2026.12.1",
    });

    fireEvent.click(version);
    await screen.findByText("Не удалось загрузить список изменений");
    fireEvent.click(screen.getByRole("button", { name: "Повторить" }));

    await screen.findByText("Полная переработка системы");
    expect(api.history).toHaveBeenCalledTimes(3);
    expect(window.localStorage.getItem(releaseNotesSeenKey("staff-a"))).toBe(current.version);
  });

  it("closes shown notes when browser storage is unavailable without claiming persistence", async () => {
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("unavailable", "SecurityError");
    });
    const view = render(
      <ReleaseNotesProvider staffId="staff-a" canViewHistory enabled>
        <ReleaseVersion />
      </ReleaseNotesProvider>,
    );
    await screen.findByText("Полная переработка системы");

    fireEvent.click(screen.getByRole("button", { name: "Понятно" }));

    const closingDialog = screen.getByRole("dialog");
    await waitFor(() => expect(closingDialog.className).toContain("ant-zoom-leave-active"));
    fireEvent.transitionEnd(closingDialog);
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(window.localStorage.getItem(releaseNotesSeenKey("staff-a"))).toBeNull();
    expect(Storage.prototype.setItem).toHaveBeenCalled();

    view.rerender(
      <ReleaseNotesProvider staffId="staff-a" canViewHistory enabled>
        <ReleaseVersion />
      </ReleaseNotesProvider>,
    );
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("acknowledges release notes for the account selected after a provider switch", async () => {
    window.localStorage.setItem(releaseNotesSeenKey("staff-a"), current.version);
    const view = render(
      <ReleaseNotesProvider staffId="staff-a" canViewHistory enabled>
        <ReleaseVersion />
      </ReleaseNotesProvider>,
    );
    await waitFor(() => expect(api.history).toHaveBeenCalledOnce());
    expect(screen.queryByRole("dialog")).toBeNull();

    view.rerender(
      <ReleaseNotesProvider staffId="staff-b" canViewHistory enabled>
        <ReleaseVersion />
      </ReleaseNotesProvider>,
    );
    await screen.findByText("Полная переработка системы");
    fireEvent.click(screen.getByRole("button", { name: "Понятно" }));

    await waitFor(() =>
      expect(window.localStorage.getItem(releaseNotesSeenKey("staff-b"))).toBe(current.version),
    );
    expect(window.localStorage.getItem(releaseNotesSeenKey("staff-a"))).toBe(current.version);
  });
});
