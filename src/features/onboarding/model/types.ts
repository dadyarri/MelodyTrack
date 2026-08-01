import type { TourProps } from "antd";

export const ONBOARDING_DEFINITION_VERSION = 3;

export const onboardingTargetIds = [
  "dashboard-content",
  "tasks-content",
  "schedule-header-actions",
  "schedule-calendar",
  "clients-page-content",
  "courses-workspace",
  "statistics-main",
  "users-page-content",
  "audit-page-content",
  "profile-availability",
] as const;

export type OnboardingTargetId = (typeof onboardingTargetIds)[number];
export type OnboardingJourneyId = "teacher" | "administrator" | "superuser";
export type OnboardingRoute =
  | "/"
  | "/tasks"
  | "/schedule"
  | "/clients"
  | "/courses"
  | "/statistics/work"
  | "/users"
  | "/audit"
  | "/profile";

export type OnboardingStepDefinition = {
  id: string;
  path: OnboardingRoute;
  targetId?: OnboardingTargetId;
  title: string;
  description: string;
  placement?: NonNullable<TourProps["steps"]>[number]["placement"];
};

export type OnboardingJourney = {
  id: OnboardingJourneyId;
  steps: readonly OnboardingStepDefinition[];
};
