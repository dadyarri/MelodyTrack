import {
  cleanup,
  fireEvent,
  render as renderUi,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Notifications } from "@/shared/ui/notifications";
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

function render(ui: ReactNode) {
  return renderUi(ui, {
    wrapper: ({ children }) => (
      <>
        {children}
        <Notifications appearance="light" />
      </>
    ),
  });
}

async function chooseSelectOption(name: string) {
  const option = await screen.findByRole("option", { name });
  fireEvent.pointerDown(option, { pointerType: "mouse" });
  fireEvent.click(option);
}

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

describe("legacy access training", () => {
  it("does not block the workspace or open onboarding from saved access progress", async () => {
    storage.load.mockResolvedValue(createProgress("staff-a"));
    render(
      <EmployeeTrainingProvider staffId="staff-a">
        <LaunchClientTraining />
        <TrainingWorkspace />
      </EmployeeTrainingProvider>,
    );

    await waitFor(() => expect(screen.queryByText("Загрузка…")).toBeNull());

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.getByLabelText("Учебное окно").textContent).toBe("закрыто");
    expect(screen.queryByText("Вход и сессия")).toBeNull();
    expect(screen.queryByRole("button", { name: "Начать обучение" })).toBeNull();
  });

  it("keeps each exercise launch inside its nested topic accordion", async () => {
    storage.load.mockResolvedValue({
      ...createProgress("staff-a"),
      demoStep: "completed",
      clientDirectory: { version: 1, completed: true, deferred: false },
    });
    render(
      <EmployeeTrainingProvider staffId="staff-a" canUseClientDirectory>
        <TrainingWorkspace />
      </EmployeeTrainingProvider>,
    );

    const topic = await screen.findByRole("button", { name: "Список клиентов" });
    fireEvent.click(topic);
    const searchTopic = await screen.findByRole("button", { name: "Поиск и фильтры" });
    fireEvent.click(searchTopic);

    const launch = await screen.findByRole("button", { name: "Тренировка поиска и фильтров" });
    expect(searchTopic.getAttribute("aria-expanded")).toBe("true");
    fireEvent.click(launch);

    await screen.findByRole("dialog", { name: "Список клиентов · Поиск и фильтры" });
    await screen.findByText("Учебные клиенты");
    expect(within(screen.getByRole("dialog")).queryByLabelText("Учебная фамилия")).toBeNull();
  });

  it.each([
    ["Сортировка таблицы", "Тренировка сортировки", "sorting"],
    ["Карточка клиента", "Тренировка карточки", "card"],
  ])("opens only the exercise belonging to %s", async (topic, launch, kind) => {
    storage.load.mockResolvedValue({
      ...createProgress("staff-a"),
      clientDirectory: { version: 1, completed: true, deferred: false },
    });
    render(
      <EmployeeTrainingProvider staffId="staff-a" canUseClientDirectory>
        <TrainingWorkspace />
      </EmployeeTrainingProvider>,
    );
    fireEvent.click(await screen.findByRole("button", { name: "Список клиентов" }));
    fireEvent.click(await screen.findByRole("button", { name: topic }));
    fireEvent.click(await screen.findByRole("button", { name: launch }));

    const dialog = within(await screen.findByRole("dialog"));
    if (kind === "card") {
      expect(dialog.getByLabelText("Учебная фамилия")).toBeDefined();
      expect(dialog.queryByRole("searchbox")).toBeNull();
    } else {
      expect(dialog.getByRole("button", { name: "Сортировка: ФИО" })).toBeDefined();
      expect(dialog.getByRole("searchbox", { name: "Поиск" })).toHaveProperty(
        "value",
        "Иванов Антон",
      );
      expect(dialog.queryByLabelText("Учебная фамилия")).toBeNull();
    }
    expect(storage.save).not.toHaveBeenCalled();
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
    await screen.findByText("Проверьте поиск, источник и состояние работы.");
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

  it("completes the demonstrated search, filters, and descending order, then restores the saved result", async () => {
    let persisted = createProgress("staff-a");
    storage.load.mockImplementation(async () => persisted);
    storage.save.mockImplementation(async (next) => {
      persisted = next;
    });
    const view = render(
      <EmployeeTrainingProvider staffId="staff-a" canUseClientDirectory>
        <LaunchClientTraining />
      </EmployeeTrainingProvider>,
    );

    fireEvent.click(await screen.findByRole("button", { name: "Открыть учебный список" }));
    await screen.findByText("Учебные клиенты");
    fireEvent.change(screen.getByRole("searchbox", { name: "Поиск" }), {
      target: { value: "Иванов Антон" },
    });
    fireEvent.click(screen.getByRole("combobox", { name: "Учебный источник" }));
    await chooseSelectOption("Рекомендация");
    fireEvent.click(screen.getByRole("combobox", { name: "Учебное состояние работы" }));
    await chooseSelectOption("В работе");
    const result = screen.getByRole("button", { name: "Проверить результат" });
    fireEvent.click(result);

    await screen.findByText("Тренировка пройдена");
    expect(persisted.clientDirectory.completed).toBe(false);
    expect(storage.save).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "К сортировке" }));
    fireEvent.click(await screen.findByRole("button", { name: "Проверить результат" }));
    await screen.findByText("Проверьте порядок фамилий: от Я к А.");
    fireEvent.click(screen.getByRole("button", { name: "Сортировка: ФИО" }));
    fireEvent.click(screen.getByRole("button", { name: "Проверить результат" }));

    await screen.findByText("Тренировка пройдена");
    await waitFor(() => expect(persisted.clientDirectory.completed).toBe(true));
    expect(persisted).toMatchObject({
      staffId: "staff-a",
      demoStep: "signed-out",
      articleRead: false,
      clientDirectory: { version: 1, completed: true, deferred: false },
    });
    expect(storage.save).toHaveBeenCalledWith(
      expect.objectContaining({
        clientDirectory: { version: 1, completed: true, deferred: false },
      }),
      expect.any(AbortSignal),
    );
    expect(fetch).not.toHaveBeenCalled();

    view.unmount();
    render(
      <EmployeeTrainingProvider staffId="staff-a" canUseClientDirectory>
        <LaunchClientTraining />
      </EmployeeTrainingProvider>,
    );
    fireEvent.click(await screen.findByRole("button", { name: "Открыть учебный список" }));
    await screen.findByText("Учебные клиенты");
    expect(screen.getByText("clients-complete")).toBeDefined();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("saves an incomplete exercise as deferred and reports a failed local write", async () => {
    storage.load.mockResolvedValue(createProgress("staff-a"));
    const view = render(
      <EmployeeTrainingProvider staffId="staff-a" canUseClientDirectory>
        <LaunchClientTraining />
      </EmployeeTrainingProvider>,
    );
    fireEvent.click(await screen.findByRole("button", { name: "Открыть учебный список" }));
    await screen.findByText("Учебные клиенты");
    fireEvent.click(screen.getByRole("button", { name: "Отложить" }));
    await waitFor(() =>
      expect(storage.save).toHaveBeenCalledWith(
        expect.objectContaining({
          clientDirectory: { version: 1, completed: false, deferred: true },
        }),
        expect.any(AbortSignal),
      ),
    );
    await waitFor(() => expect(screen.getByLabelText("Учебное окно").textContent).toBe("закрыто"));

    view.unmount();
    storage.load.mockResolvedValue(createProgress("staff-a"));
    storage.save.mockRejectedValue(new Error("storage unavailable"));
    render(
      <EmployeeTrainingProvider staffId="staff-a" canUseClientDirectory>
        <LaunchClientTraining />
      </EmployeeTrainingProvider>,
    );
    fireEvent.click(await screen.findByRole("button", { name: "Открыть учебный список" }));
    await screen.findByText("Учебные клиенты");
    fireEvent.change(screen.getByRole("searchbox", { name: "Поиск" }), {
      target: { value: "Иванов Антон" },
    });
    fireEvent.click(screen.getByRole("combobox", { name: "Учебный источник" }));
    await chooseSelectOption("Рекомендация");
    fireEvent.click(screen.getByRole("combobox", { name: "Учебное состояние работы" }));
    await chooseSelectOption("В работе");
    fireEvent.click(screen.getByRole("button", { name: "Проверить результат" }));

    await screen.findByText("Тренировка пройдена");
    fireEvent.click(screen.getByRole("button", { name: "К сортировке" }));
    fireEvent.click(await screen.findByRole("button", { name: "Сортировка: ФИО" }));
    fireEvent.click(screen.getByRole("button", { name: "Проверить результат" }));

    await screen.findByText("Тренировка пройдена");
    await screen.findByText("Не удалось сохранить прогресс обучения.");
    expect(screen.getByText("clients-complete")).toBeDefined();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("does not apply an earlier account progress after the owner changes", async () => {
    let finishFirstLoad: ((value: ReturnType<typeof createProgress>) => void) | undefined;
    storage.load.mockImplementation((staffId) =>
      staffId === "staff-a"
        ? new Promise((resolve) => {
            finishFirstLoad = resolve;
          })
        : Promise.resolve(createProgress(staffId)),
    );
    const view = render(
      <EmployeeTrainingProvider staffId="staff-a" canUseClientDirectory>
        <LaunchClientTraining />
      </EmployeeTrainingProvider>,
    );
    await screen.findByText("Загрузка…");

    view.rerender(
      <EmployeeTrainingProvider staffId="staff-b" canUseClientDirectory>
        <LaunchClientTraining />
      </EmployeeTrainingProvider>,
    );
    await waitFor(() => expect(screen.getByText("clients-incomplete")).toBeDefined());
    const oldProgress = {
      ...createProgress("staff-a"),
      clientDirectory: { version: 1 as const, completed: true, deferred: true },
    };
    finishFirstLoad?.(oldProgress);

    await waitFor(() => expect(screen.getByText("clients-incomplete")).toBeDefined());
    expect(screen.queryByText("clients-complete")).toBeNull();
    expect(storage.save).not.toHaveBeenCalled();
  });
});
