import type { AppUser } from "@/entities/session";

import { administratorJourney, superuserJourney, teacherJourney } from "../config/staffJourneys";
import { type OnboardingJourney, onboardingTargetIds } from "./types";

export const onboardingJourneys = [teacherJourney, administratorJourney, superuserJourney] as const;

export function getOnboardingJourney(user: AppUser): OnboardingJourney {
  if (user?.isSuperuser) {
    return superuserJourney;
  }

  if (user?.isAdmin) {
    return administratorJourney;
  }

  return teacherJourney;
}

export function getOnboardingStepIndex(journey: OnboardingJourney, stepId?: string | null) {
  const index = journey.steps.findIndex((step) => step.id === stepId);
  return index >= 0 ? index : 0;
}

export function validateOnboardingJourneys(journeys: readonly OnboardingJourney[] = onboardingJourneys) {
  const issues: string[] = [];
  const stepIds = new Set<string>();
  const knownTargets = new Set<string>(onboardingTargetIds);

  for (const journey of journeys) {
    if (journey.steps.length === 0) {
      issues.push(`Journey ${journey.id} has no steps.`);
    }

    for (const step of journey.steps) {
      if (stepIds.has(step.id)) {
        issues.push(`Step id ${step.id} is duplicated.`);
      }
      stepIds.add(step.id);

      if (!step.path.startsWith("/")) {
        issues.push(`Step ${step.id} has an invalid path.`);
      }
      if (step.targetId && !knownTargets.has(step.targetId)) {
        issues.push(`Step ${step.id} uses an unknown target ${step.targetId}.`);
      }
      if (!step.title.trim() || !step.description.trim()) {
        issues.push(`Step ${step.id} has empty copy.`);
      }
    }
  }

  return issues;
}
