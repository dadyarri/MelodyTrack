import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createProgress } from "../model/progress";
import { EmployeeTrainingProvider, useEmployeeTraining } from "./EmployeeTraining";

const storage = vi.hoisted(() => ({ load: vi.fn(), save: vi.fn() }));
vi.mock("../model/storage", () => ({ loadTrainingProgress: storage.load, saveTrainingProgress: storage.save }));

beforeEach(() => {
  storage.load.mockReset();
  storage.save.mockReset().mockResolvedValue(undefined);
  vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: false, addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn() })));
  vi.stubGlobal("fetch", vi.fn());
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

function LaunchTraining() {
  const { openTraining, progress } = useEmployeeTraining();
  return <><button type="button" onClick={openTraining}>Запустить обучение</button><output>{progress?.demoStep}</output></>;
}

describe("training account entry", () => {
  it("opens the demo account with one click without credential fields or API requests", async () => {
    storage.load.mockResolvedValue(createProgress("staff-a"));
    render(<EmployeeTrainingProvider staffId="staff-a"><LaunchTraining /></EmployeeTrainingProvider>);
    const start = await screen.findByRole("button", { name: "Начать обучение", exact: true });

    fireEvent.click(start);

    await screen.findByText("Учебная сессия открыта");
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(fetch).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Выйти из демонстрации", exact: true }));
    await screen.findByText("Тренировка пройдена");
    expect(fetch).not.toHaveBeenCalled();
  });

  it("starts a replay from its launch button without replacing completed progress", async () => {
    storage.load.mockResolvedValue({ ...createProgress("staff-a"), demoStep: "completed", articleRead: true });
    render(<EmployeeTrainingProvider staffId="staff-a"><LaunchTraining /></EmployeeTrainingProvider>);
    const launch = await screen.findByRole("button", { name: "Запустить обучение", exact: true });
    await waitFor(() => expect(screen.getByRole("status").textContent).toBe("completed"));

    fireEvent.click(launch);

    await screen.findByText("Учебная сессия открыта");
    expect(screen.getByRole("status").textContent).toBe("completed");
    expect(storage.save).not.toHaveBeenCalled();
    expect(screen.queryByRole("textbox")).toBeNull();
  });
});
