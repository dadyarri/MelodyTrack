import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { usePortalAppearance } from "./PortalAppearanceContext";
import { PortalAppearanceProvider } from "./PortalAppearanceProvider";
import { portalAppearanceStorageKey } from "./storage";

beforeEach(() => {
  localStorage.removeItem(portalAppearanceStorageKey);
});
afterEach(() => {
  vi.restoreAllMocks();
  localStorage.removeItem(portalAppearanceStorageKey);
});

function PreferenceHarness() {
  const { appearance, warning, setAppearance } = usePortalAppearance();
  return (
    <>
      <p data-testid="appearance">{appearance}</p>
      <button
        type="button"
        onClick={() => {
          setAppearance("minimal");
        }}
      >
        Упростить
      </button>
      <span role="status">{warning}</span>
    </>
  );
}

describe("Portal appearance preference", () => {
  it("restores the selected appearance after remounting", () => {
    const first = render(
      <PortalAppearanceProvider>
        <PreferenceHarness />
      </PortalAppearanceProvider>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Упростить" }));
    first.unmount();
    render(
      <PortalAppearanceProvider>
        <PreferenceHarness />
      </PortalAppearanceProvider>,
    );

    expect(localStorage.getItem(portalAppearanceStorageKey)).toBe("minimal");
    expect(screen.getByTestId("appearance")).toHaveTextContent("minimal");
  });

  it("falls back to the RPG appearance for an unrecognized stored value", () => {
    localStorage.setItem(portalAppearanceStorageKey, "invalid");

    render(
      <PortalAppearanceProvider>
        <PreferenceHarness />
      </PortalAppearanceProvider>,
    );

    expect(screen.getByTestId("appearance")).toHaveTextContent("rpg");
  });

  it("changes appearance and explains when saving is unavailable", () => {
    vi.spyOn(localStorage, "setItem").mockImplementation(() => {
      throw new DOMException("Denied", "SecurityError");
    });
    render(
      <PortalAppearanceProvider>
        <PreferenceHarness />
      </PortalAppearanceProvider>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Упростить" }));

    expect(screen.getByTestId("appearance")).toHaveTextContent("minimal");
    expect(screen.getByRole("status")).toHaveTextContent("Не удалось сохранить оформление");
  });

  it("renders a usable default when reading storage is denied", () => {
    vi.spyOn(localStorage, "getItem").mockImplementation(() => {
      throw new DOMException("Denied", "SecurityError");
    });

    render(
      <PortalAppearanceProvider>
        <PreferenceHarness />
      </PortalAppearanceProvider>,
    );

    expect(screen.getByTestId("appearance")).toHaveTextContent("rpg");
    expect(screen.getByRole("status")).toHaveTextContent("Хранилище браузера недоступно");
  });

  it("synchronizes a preference changed in another tab", () => {
    render(
      <PortalAppearanceProvider>
        <PreferenceHarness />
      </PortalAppearanceProvider>,
    );

    fireEvent(window, new StorageEvent("storage", { key: portalAppearanceStorageKey, newValue: "minimal" }));

    expect(screen.getByTestId("appearance")).toHaveTextContent("minimal");
  });
});
