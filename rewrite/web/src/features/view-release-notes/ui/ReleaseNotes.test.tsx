import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { releaseNotesSeenKey } from "../model/releaseNotesStorage";
import { ReleaseNotesProvider, ReleaseVersion } from "./ReleaseNotes";

const api = vi.hoisted(() => ({ current: vi.fn(), history: vi.fn() }));
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
});
