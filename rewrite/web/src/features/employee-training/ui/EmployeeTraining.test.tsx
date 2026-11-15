import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createProgress } from "../model/progress";
import {
  EmployeeTrainingProvider,
  TrainingWorkspace,
  useEmployeeTraining,
} from "./EmployeeTraining";

const storage = vi.hoisted(() => ({ load: vi.fn(), save: vi.fn() }));
vi.mock("../model/storage", () => ({
  loadTrainingProgress: storage.load,
  saveTrainingProgress: storage.save,
}));

beforeEach(() => {
  storage.load.mockReset();
  storage.save.mockReset().mockResolvedValue(undefined);
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
  vi.stubGlobal("fetch", vi.fn());
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function LaunchTraining() {
  const { openTraining, progress } = useEmployeeTraining();
  return (
    <>
      <button type="button" onClick={openTraining}>
        Запустить обучение
      </button>
      <output>{progress?.demoStep}</output>
    </>
  );
}

function LaunchClientTraining() {
  const { openClientTraining, progress, active } = useEmployeeTraining();
  return (
    <>
      <button type="button" onClick={openClientTraining}>
        Открыть учебный список
      </button>
      <output aria-label="Учебное окно">{active ? "открыто" : "закрыто"}</output>
      <output>
        {progress?.clientDirectory.completed ? "clients-complete" : "clients-incomplete"}
      </output>
    </>
  );
}

describe("training account entry", () => {
  it("opens the demo account with one click without credential fields or API requests", async () => {
    storage.load.mockResolvedValue(createProgress("staff-a"));
    render(
      <EmployeeTrainingProvider staffId="staff-a">
        <LaunchTraining />
      </EmployeeTrainingProvider>,
    );
    const start = await screen.findByRole("button", { name: "Начать обучение" });

    fireEvent.click(start);

    await screen.findByText("Учебная сессия открыта");
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(fetch).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Выйти из демонстрации" }));
    await screen.findByText("Тренировка пройдена");
    expect(fetch).not.toHaveBeenCalled();
  });

  it("starts a replay from its launch button without replacing completed progress", async () => {
    storage.load.mockResolvedValue({
      ...createProgress("staff-a"),
      demoStep: "completed",
      articleRead: true,
    });
    render(
      <EmployeeTrainingProvider staffId="staff-a">
        <LaunchTraining />
      </EmployeeTrainingProvider>,
    );
    const launch = await screen.findByRole("button", { name: "Запустить обучение" });
    await waitFor(() => expect(screen.getByRole("status").textContent).toBe("completed"));

    fireEvent.click(launch);

    await screen.findByText("Учебная сессия открыта");
    expect(screen.getByRole("status").textContent).toBe("completed");
    expect(storage.save).not.toHaveBeenCalled();
    expect(screen.queryByRole("textbox")).toBeNull();
  });
});

describe("client directory module", () => {
  it("opens synthetic clients without completing the exercise or resetting access progress", async () => {
    storage.load.mockResolvedValue({
      ...createProgress("staff-a"),
      demoStep: "completed",
      articleRead: true,
    });
    render(
      <EmployeeTrainingProvider staffId="staff-a" canUseClientDirectory>
        <LaunchClientTraining />
      </EmployeeTrainingProvider>,
    );
    const launch = await screen.findByRole("button", { name: "Открыть учебный список" });

    fireEvent.click(launch);

    await screen.findByText("Учебные клиенты");
    fireEvent.click(screen.getByRole("button", { name: "Проверить результат" }));
    await screen.findByText("Проверьте поиск, источник, состояние работы и порядок фамилий.");
    fireEvent.click(screen.getByRole("tab", { name: "Инструкция" }));
    await screen.findByText(/Поиск находит клиентов по полному ФИО/);
    expect(screen.getByText("clients-incomplete")).toBeDefined();
    expect(storage.save).not.toHaveBeenCalled();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("does not offer or open the client exercise without the current permission", async () => {
    storage.load.mockResolvedValue({ ...createProgress("staff-a"), demoStep: "completed" });
    render(
      <EmployeeTrainingProvider staffId="staff-a">
        <LaunchClientTraining />
        <TrainingWorkspace />
      </EmployeeTrainingProvider>,
    );
    const launch = await screen.findByRole("button", { name: "Открыть учебный список" });

    fireEvent.click(launch);

    expect(screen.queryByText("Учебные клиенты")).toBeNull();
    expect(screen.queryByText("Список клиентов")).toBeNull();
    expect(storage.save).not.toHaveBeenCalled();
  });

  it("closes a revoked client exercise while preserving its completed history", async () => {
    const completed = {
      ...createProgress("staff-a"),
      demoStep: "completed",
      clientDirectory: { version: 1, completed: true, deferred: false },
    };
    storage.load.mockResolvedValue(completed);
    const view = render(
      <EmployeeTrainingProvider staffId="staff-a" canUseClientDirectory>
        <LaunchClientTraining />
      </EmployeeTrainingProvider>,
    );
    fireEvent.click(await screen.findByRole("button", { name: "Открыть учебный список" }));
    await screen.findByText("Учебные клиенты");

    view.rerender(
      <EmployeeTrainingProvider staffId="staff-a" canUseClientDirectory={false}>
        <LaunchClientTraining />
      </EmployeeTrainingProvider>,
    );

    await waitFor(() => expect(screen.getByLabelText("Учебное окно").textContent).toBe("закрыто"));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.queryByRole("button", { name: "Проверить результат" })).toBeNull();
    expect(screen.getByText("clients-complete")).toBeDefined();
    expect(storage.save).not.toHaveBeenCalled();
  });
});
