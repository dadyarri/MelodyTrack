import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { App as AntdApp, Button, ConfigProvider } from "antd";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { getReleaseHistory, type ReleaseEntry, type ReleaseHistory } from "@/entities/release";

import { getReleaseNotesSeenKey } from "../model/releaseNotesStorage";
import { useReleaseNotesController } from "../model/useReleaseNotesController";
import { ReleaseNotesModal, ReleaseVersion } from "./ReleaseNotes";

vi.mock("@/entities/release", () => ({
  getReleaseHistory: vi.fn(),
  releaseQueryKeys: { all: ["releases"], history: (page = 1) => ["releases", "history", page] },
}));

const currentRelease: ReleaseEntry = {
  version: "2026.07.1.1",
  codename: "Accordatura",
  date: "2026-07-29",
  parentVersion: "2026.07.1",
  changes: { new: ["Автоматическая история обновлений"], improved: [], fixed: ["Продление сеанса"], security: [] },
};
const previousRelease: ReleaseEntry = {
  version: "2026.07.1",
  codename: "Accordatura",
  date: "2026-07-29",
  parentVersion: null,
  changes: { new: ["История обновлений"], improved: [], fixed: [], security: [] },
};
const history = createHistory([currentRelease, previousRelease]);

describe("ReleaseNotes", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.clearAllMocks();
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it("shows the current patch automatically and acknowledges it only after dismissal", async () => {
    localStorage.setItem(getReleaseNotesSeenKey("user-1"), "2026.07.1");
    vi.mocked(getReleaseHistory).mockResolvedValue(history);
    renderReleaseNotes(<ReleaseNotesExperience userId="user-1" automaticEnabled />);

    expect(await screen.findByText("Исправление 2026.07.1.1 — Accordatura")).toBeInTheDocument();
    expect(screen.getByText("Автоматическая история обновлений")).toBeInTheDocument();
    expect(screen.queryByText("История обновлений")).not.toBeInTheDocument();
    expect(localStorage.getItem(getReleaseNotesSeenKey("user-1"))).toBe("2026.07.1");

    fireEvent.click(screen.getByRole("button", { name: "Понятно" }));

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Что нового" })).not.toBeInTheDocument());
    expect(localStorage.getItem(getReleaseNotesSeenKey("user-1"))).toBe("2026.07.1.1");
  });

  it("does not automatically show an acknowledged release but keeps manual history available", async () => {
    localStorage.setItem(getReleaseNotesSeenKey("user-1"), "2026.07.1.1");
    vi.mocked(getReleaseHistory).mockResolvedValue(history);
    renderReleaseNotes(<ReleaseNotesExperience userId="user-1" automaticEnabled />);

    await waitFor(() => {
      expect(getReleaseHistory).toHaveBeenCalled();
    });
    expect(screen.queryByRole("dialog", { name: "Что нового" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Открыть историю" }));

    expect(await screen.findByText("Исправление 2026.07.1.1 — Accordatura")).toBeInTheDocument();
    expect(screen.getByText("2026.07.1 — Accordatura")).toBeInTheDocument();
  });

  it("defers automatic notes until onboarding is no longer active", async () => {
    vi.mocked(getReleaseHistory).mockResolvedValue(history);
    const view = renderReleaseNotes(<ReleaseNotesExperience userId="user-1" automaticEnabled={false} />);

    await waitFor(() => {
      expect(getReleaseHistory).toHaveBeenCalled();
    });
    expect(screen.queryByRole("dialog", { name: "Что нового" })).not.toBeInTheDocument();

    view.rerender(wrapReleaseNotes(<ReleaseNotesExperience userId="user-1" automaticEnabled />));

    expect(await screen.findByText("Исправление 2026.07.1.1 — Accordatura")).toBeInTheDocument();
  });

  it("does not leak acknowledgement between accounts", async () => {
    localStorage.setItem(getReleaseNotesSeenKey("user-1"), "2026.07.1.1");
    vi.mocked(getReleaseHistory).mockResolvedValue(history);
    const view = renderReleaseNotes(<ReleaseNotesExperience userId="user-1" automaticEnabled />);

    await waitFor(() => {
      expect(getReleaseHistory).toHaveBeenCalled();
    });
    expect(screen.queryByRole("dialog", { name: "Что нового" })).not.toBeInTheDocument();

    view.rerender(wrapReleaseNotes(<ReleaseNotesExperience userId="user-2" automaticEnabled />));

    expect(await screen.findByText("Исправление 2026.07.1.1 — Accordatura")).toBeInTheDocument();
  });

  it("closes automatic notes when another tab acknowledges the same release", async () => {
    vi.mocked(getReleaseHistory).mockResolvedValue(history);
    renderReleaseNotes(<ReleaseNotesExperience userId="user-1" automaticEnabled />);
    expect(await screen.findByText("Исправление 2026.07.1.1 — Accordatura")).toBeInTheDocument();

    const key = getReleaseNotesSeenKey("user-1");
    localStorage.setItem(key, "2026.07.1.1");
    window.dispatchEvent(new StorageEvent("storage", { key, newValue: "2026.07.1.1" }));

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Что нового" })).not.toBeInTheDocument());
  });

  it("treats malformed acknowledgement as unseen", async () => {
    localStorage.setItem(getReleaseNotesSeenKey("user-1"), "latest");
    vi.mocked(getReleaseHistory).mockResolvedValue(history);
    renderReleaseNotes(<ReleaseNotesExperience userId="user-1" automaticEnabled />);

    expect(await screen.findByText("Исправление 2026.07.1.1 — Accordatura")).toBeInTheDocument();
    expect(screen.getByText("2026.07.1 — Accordatura")).toBeInTheDocument();
  });

  it("shows every release after the last acknowledged entry across history pages", async () => {
    const olderRelease: ReleaseEntry = {
      version: "2026.06.1",
      codename: "Aria",
      date: "2026-06-20",
      parentVersion: null,
      changes: { new: ["Старое обновление"], improved: [], fixed: [], security: [] },
    };
    localStorage.setItem(getReleaseNotesSeenKey("user-1"), olderRelease.version);
    const firstPage = createHistory([currentRelease], { page: 1, pageSize: 1, totalCount: 3, totalPages: 2, hasNextPage: true });
    const secondPage = createHistory([previousRelease, olderRelease], {
      page: 2,
      pageSize: 2,
      totalCount: 3,
      totalPages: 2,
      hasNextPage: false,
    });
    vi.mocked(getReleaseHistory).mockImplementation((page = 1) => Promise.resolve(page === 1 ? firstPage : secondPage));
    renderReleaseNotes(<ReleaseNotesExperience userId="user-1" automaticEnabled />);

    expect(await screen.findByText("Исправление 2026.07.1.1 — Accordatura")).toBeInTheDocument();
    expect(screen.getByText("2026.07.1 — Accordatura")).toBeInTheDocument();
    expect(screen.queryByText("2026.06.1 — Aria")).not.toBeInTheDocument();
    expect(getReleaseHistory).toHaveBeenCalledWith(2);
  });

  it("shows a newer named release after an acknowledged patch", async () => {
    const newerRelease: ReleaseEntry = {
      version: "2026.08.1",
      codename: "Ballata",
      date: "2026-08-03",
      parentVersion: null,
      changes: { new: ["Новая версия"], improved: [], fixed: [], security: [] },
    };
    localStorage.setItem(getReleaseNotesSeenKey("user-1"), "2026.07.1.1");
    vi.mocked(getReleaseHistory).mockResolvedValue({
      ...createHistory([newerRelease]),
      currentVersion: newerRelease.version,
    });
    renderReleaseNotes(<ReleaseNotesExperience userId="user-1" automaticEnabled />);

    expect(await screen.findByText("2026.08.1 — Ballata")).toBeInTheDocument();
  });

  it("closes quietly but does not acknowledge when storage is unavailable", async () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("Storage blocked", "SecurityError");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("Storage blocked", "SecurityError");
    });
    vi.mocked(getReleaseHistory).mockResolvedValue(history);
    renderReleaseNotes(<ReleaseNotesExperience userId="user-1" automaticEnabled />);
    expect(await screen.findByText("Исправление 2026.07.1.1 — Accordatura")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Понятно" }));

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Что нового" })).not.toBeInTheDocument());
  });

  it("does not open an automatic error modal when the release API fails", async () => {
    vi.mocked(getReleaseHistory).mockRejectedValue(new Error("offline"));
    renderReleaseNotes(<ReleaseNotesExperience userId="user-1" automaticEnabled />);

    await waitFor(() => {
      expect(getReleaseHistory).toHaveBeenCalled();
    });
    expect(screen.queryByRole("dialog", { name: "Что нового" })).not.toBeInTheDocument();
    expect(localStorage.getItem(getReleaseNotesSeenKey("user-1"))).toBeNull();
  });

  it("shows an API failure inside manually opened history without acknowledging it", async () => {
    vi.mocked(getReleaseHistory).mockRejectedValue(new Error("offline"));
    renderReleaseNotes(<ReleaseNotesModal open onClose={vi.fn()} />);

    expect(await screen.findByText("Не удалось загрузить историю обновлений", {}, { timeout: 3000 })).toBeInTheDocument();
  });

  it("pages through the complete manual history", async () => {
    vi.stubGlobal("matchMedia", (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }));
    localStorage.setItem(getReleaseNotesSeenKey("user-1"), "2026.07.1.1");
    const firstPage = createHistory([currentRelease], { page: 1, pageSize: 1, totalCount: 2, totalPages: 2, hasNextPage: true });
    const secondPage = createHistory([previousRelease], { page: 2, pageSize: 1, totalCount: 2, totalPages: 2, hasNextPage: false });
    vi.mocked(getReleaseHistory).mockImplementation((page = 1) => Promise.resolve(page === 1 ? firstPage : secondPage));
    renderReleaseNotes(<ReleaseNotesExperience userId="user-1" automaticEnabled />);
    await waitFor(() => {
      expect(getReleaseHistory).toHaveBeenCalled();
    });

    fireEvent.click(screen.getByRole("button", { name: "Открыть историю" }));
    expect(await screen.findByText("Исправление 2026.07.1.1 — Accordatura")).toBeInTheDocument();
    fireEvent.click(screen.getByTitle("2"));

    expect(await screen.findByText("2026.07.1 — Accordatura")).toBeInTheDocument();
    expect(getReleaseHistory).toHaveBeenCalledWith(2);
  });

  it("renders the version separately from the release-notes button", async () => {
    vi.mocked(getReleaseHistory).mockResolvedValue(history);
    renderReleaseNotes(<ReleaseVersion />);
    expect(await screen.findByText("Версия 2026.07.1.1")).toBeInTheDocument();
    expect(screen.queryByRole("dialog", { name: "Что нового" })).not.toBeInTheDocument();
  });
});

function ReleaseNotesExperience({ userId, automaticEnabled }: { userId: string; automaticEnabled: boolean }) {
  const controller = useReleaseNotesController({ userId, automaticEnabled });
  return (
    <>
      <Button onClick={controller.openManual}>Открыть историю</Button>
      <ReleaseNotesModal open={controller.open} automaticReleases={controller.automaticReleases} onClose={controller.close} />
    </>
  );
}

function createHistory(
  releases: ReleaseEntry[],
  pagination: Partial<Pick<ReleaseHistory, "page" | "pageSize" | "totalCount" | "totalPages" | "hasNextPage">> = {},
): ReleaseHistory {
  return {
    currentVersion: "2026.07.1.1",
    releases,
    page: 1,
    pageSize: 2,
    totalCount: releases.length,
    totalPages: 1,
    hasNextPage: false,
    ...pagination,
  };
}

function wrapReleaseNotes(content: ReactNode) {
  return (
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ConfigProvider theme={{ token: { motion: false } }}>
        <AntdApp>{content}</AntdApp>
      </ConfigProvider>
    </QueryClientProvider>
  );
}

function renderReleaseNotes(content: ReactNode) {
  return render(wrapReleaseNotes(content));
}
