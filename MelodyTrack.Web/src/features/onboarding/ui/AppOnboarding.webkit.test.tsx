import "../../../app/styles/index.css";
import "../../../app/styles/mobile-compatibility.css";

import { describe, expect, it, vi } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import { administratorJourney } from "../config/staffJourneys";
import type { OnboardingController } from "../model/useOnboardingController";
import { OnboardingTour } from "./AppOnboarding";

describe("onboarding tour in supported browsers", () => {
  it.each([
    { width: 1280, height: 800, label: "desktop" },
    { width: 320, height: 568, label: "mobile" },
  ])("keeps the $label journey and its actions visible", async ({ width, height }) => {
    await page.viewport(width, height);
    const changeStep = vi.fn<(nextIndex: number) => void>();
    const controller = createController(changeStep);
    const screen = await render(
      <>
        <div data-onboarding-id="dashboard-content" style={{ width: 240, height: 120, margin: 24 }}>
          Обзор
        </div>
        <OnboardingTour controller={controller} />
      </>,
    );

    await expect.element(screen.getByText("Давайте быстро освоимся")).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Пропустить", exact: true })).toBeVisible();
    const nextButton = screen.getByRole("button", { name: "Далее" });
    await expect.element(nextButton).toBeVisible();
    const nextButtonElement = nextButton.element();
    if (!(nextButtonElement instanceof HTMLButtonElement)) {
      throw new TypeError("The onboarding next action must be a button.");
    }
    nextButtonElement.click();
    await expect.poll(() => changeStep.mock.calls.length).toBe(1);
    expect(changeStep).toHaveBeenCalledWith(1);
    await expect.poll(() => document.documentElement.scrollWidth - document.documentElement.clientWidth).toBeLessThanOrEqual(0);
  });
});

function createController(changeStep: (nextIndex: number) => void): OnboardingController {
  return {
    steps: administratorJourney.steps,
    currentStepIndex: 0,
    open: true,
    isLoading: false,
    isActive: true,
    isBusy: false,
    hasError: false,
    changeStep,
    complete: vi.fn(),
    skip: vi.fn(),
    retry: vi.fn(),
  };
}
