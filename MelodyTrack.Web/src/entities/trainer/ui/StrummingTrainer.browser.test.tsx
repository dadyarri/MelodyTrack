import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import { createDefaultStrummingConfiguration, type StrummingConfiguration } from "../model/practiceExercise";
import { StrummingTrainer } from "./StrummingTrainer";
import { TrainerTimingHost } from "./TrainerTimingHost";

beforeEach(async () => {
  await page.viewport(1280, 900);
});
afterEach(async () => {
  await page.viewport(1280, 900);
});

describe("guitar strumming trainer", () => {
  it("shows the unsupported-screen message below 768 pixels", async () => {
    await page.viewport(767, 800);
    const screen = await render(<StrummingTrainer />);

    await expect.element(screen.getByText("Тренажёр пока не поддерживает небольшие экраны")).toBeVisible();
    expect(document.querySelector("canvas")).toBeNull();
  });
  it("stops playback when the window becomes too narrow", async () => {
    const onPlaybackChange = vi.fn<(running: boolean) => void>();
    const screen = await render(<StrummingTrainer onPlaybackChange={onPlaybackChange} />);
    await screen.getByRole("button", { name: "Старт", exact: true }).click();
    await expect.poll(() => onPlaybackChange.mock.lastCall?.[0]).toBe(true);

    await page.viewport(767, 800);

    await expect.element(screen.getByText("Тренажёр пока не поддерживает небольшие экраны")).toBeVisible();
    await expect.poll(() => onPlaybackChange.mock.lastCall?.[0]).toBe(false);
    expect(document.querySelector("canvas")).toBeNull();
  });
  it("makes the trainer available at 768 pixels without document overflow", async () => {
    await page.viewport(768, 900);
    const screen = await render(<StrummingTrainer />);

    await expect.element(screen.getByRole("button", { name: "Старт", exact: true })).toBeVisible();
    expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(window.innerWidth);
  });
  it("replaces an action with a rest without removing its time slot", async () => {
    const onChange = vi.fn<(configuration: StrummingConfiguration) => void>();
    const screen = await render(
      <StrummingTrainer configuration={createDefaultStrummingConfiguration()} onConfigurationChange={onChange} />,
    );

    await screen.getByRole("button", { name: "Такт 1, позиция 2: убрать удар", exact: true }).click();

    const updated = onChange.mock.lastCall?.[0];
    expect(updated?.bars[0].strokes).toHaveLength(4);
    expect(updated?.bars[0].strokes.map((stroke: { action: string }) => stroke.action)).toEqual(["down", "rest", "down", "up"]);
  });
  it("keeps course exercise settings fixed for students", async () => {
    const configuration = createDefaultStrummingConfiguration();
    const onChange = vi.fn<(configuration: StrummingConfiguration) => void>();
    const screen = await render(
      <StrummingTrainer configuration={configuration} showConfiguration={false} onConfigurationChange={onChange} />,
    );

    await expect.element(screen.getByText(/Упражнение настроено преподавателем/)).toBeVisible();
    expect(document.querySelector('input[type="number"]')).toBeNull();
    expect(document.querySelectorAll('[role="spinbutton"]')).toHaveLength(0);
    expect(document.querySelectorAll('[role="switch"]')).toHaveLength(0);
    expect(onChange).not.toHaveBeenCalled();
  });
  it("hands the shared clock to the next exercise and stops the previous one", async () => {
    const firstPlayback = vi.fn<(running: boolean) => void>();
    const secondPlayback = vi.fn<(running: boolean) => void>();
    const screen = await render(
      <TrainerTimingHost>
        <section aria-label="Первое упражнение">
          <StrummingTrainer onPlaybackChange={firstPlayback} />
        </section>
        <section aria-label="Второе упражнение">
          <StrummingTrainer onPlaybackChange={secondPlayback} />
        </section>
      </TrainerTimingHost>,
    );
    await screen.getByRole("region", { name: "Первое упражнение" }).getByRole("button", { name: "Старт", exact: true }).click();
    await expect.poll(() => firstPlayback.mock.lastCall?.[0]).toBe(true);

    await screen.getByRole("region", { name: "Второе упражнение" }).getByRole("button", { name: "Старт", exact: true }).click();

    await expect.poll(() => firstPlayback.mock.lastCall?.[0]).toBe(false);
    await expect.poll(() => secondPlayback.mock.lastCall?.[0]).toBe(true);
  });
  it("clearly disables exercise following when a manual tempo is selected", async () => {
    const screen = await render(<StrummingTrainer />);

    await screen.getByRole("switch", { name: "Следовать темпу упражнения" }).click();

    await expect.element(screen.getByText(/Ручной темп: следование упражнению/)).toBeVisible();
    await expect.element(screen.getByRole("spinbutton", { name: "Темп проигрывания, BPM" })).toBeVisible();
    await expect.element(screen.getByRole("switch", { name: "Постепенно ускорять" })).toBeDisabled();
  });
  it("renders a useful error for a damaged exercise", async () => {
    const configuration = { ...createDefaultStrummingConfiguration(), bars: [] };
    const screen = await render(<StrummingTrainer configuration={configuration} />);

    await expect.element(screen.getByText("Не удалось открыть упражнение")).toBeVisible();
    expect(document.querySelector("canvas")).toBeNull();
  });
  it("asks before resetting the pattern for a different time signature", async () => {
    const onChange = vi.fn<(configuration: StrummingConfiguration) => void>();
    const screen = await render(
      <StrummingTrainer configuration={createDefaultStrummingConfiguration()} onConfigurationChange={onChange} />,
    );

    await screen.getByText("3/4", { exact: true }).click();

    await expect.element(page.getByRole("dialog", { name: "Заменить рисунок?" })).toBeVisible();
    expect(onChange).not.toHaveBeenCalled();
    await page.getByRole("button", { name: "Оставить как есть" }).click();
    expect(onChange).not.toHaveBeenCalled();
  });
});
