import { describe, expect, it } from "vitest";

import type { MeResponse } from "@/entities/session";

import { getOnboardingJourney, getOnboardingStepIndex, onboardingJourneys, validateOnboardingJourneys } from "./journeys";

const baseUser: MeResponse = {
  id: "01JTESTUSER0000000000000000",
  email: "teacher@example.com",
  firstName: "Анна",
  lastName: "Иванова",
  roleDisplayName: "Пользователь",
  isAdmin: false,
  isSuperuser: false,
  isClientPortal: false,
  isTwoFactorEnabled: false,
  isTwoFactorRequired: false,
};

describe("onboarding journeys", () => {
  it.each([
    { flags: {}, expected: "teacher" },
    { flags: { isAdmin: true }, expected: "administrator" },
    { flags: { isAdmin: true, isSuperuser: true }, expected: "superuser" },
  ])("selects the $expected journey for the current role", ({ flags, expected }) => {
    expect(getOnboardingJourney({ ...baseUser, ...flags }).id).toBe(expected);
  });

  it("contains staff journeys only", () => {
    expect(onboardingJourneys.map((journey) => journey.id)).toEqual(["teacher", "administrator", "superuser"]);
  });

  it("keeps every journey short and its definitions valid", () => {
    expect(validateOnboardingJourneys()).toEqual([]);
    for (const journey of onboardingJourneys) {
      expect(journey.steps.length).toBeGreaterThanOrEqual(3);
      expect(journey.steps.length).toBeLessThanOrEqual(8);
    }
  });

  it("shows the complete administrator journey to superusers before superuser-only guidance", () => {
    const administratorJourney = getOnboardingJourney({ ...baseUser, isAdmin: true });
    const superuserJourney = getOnboardingJourney({ ...baseUser, isAdmin: true, isSuperuser: true });

    expect(superuserJourney.steps.slice(0, administratorJourney.steps.length).map(({ id: _, ...step }) => step)).toEqual(
      administratorJourney.steps.map(({ id: _, ...step }) => step),
    );
    expect(superuserJourney.steps.slice(administratorJourney.steps.length).map((step) => step.id)).toEqual([
      "superuser-users",
      "superuser-audit",
    ]);
  });

  it("falls back to the first role-specific step when old progress cannot be resumed", () => {
    const journey = getOnboardingJourney(baseUser);

    expect(getOnboardingStepIndex(journey, "removed-step")).toBe(0);
    expect(getOnboardingStepIndex(journey, journey.steps[2]?.id)).toBe(2);
  });

  it("keeps user-facing copy free of implementation language", () => {
    const technicalLanguage = /\b(?:API|HTTP|JSON|endpoint|localStorage)\b|бэкенд|фронтенд|компонент|запрос к серверу/i;

    for (const journey of onboardingJourneys) {
      for (const step of journey.steps) {
        expect(`${step.title} ${step.description}`).not.toMatch(technicalLanguage);
      }
    }
  });
});
