import type { OnboardingTargetId } from "./types";

export function findOnboardingTarget(targetId: OnboardingTargetId, root: ParentNode = document) {
  const candidates = root.querySelectorAll<HTMLElement>(`[data-onboarding-id="${targetId}"]`);
  return [...candidates].find(isVisibleElement) ?? null;
}

export function isVisibleElement(element: HTMLElement) {
  const style = window.getComputedStyle(element);
  return style.display !== "none" && style.visibility !== "hidden" && element.getClientRects().length > 0;
}

export function getMissingTargetRecoveryIndex(currentIndex: number, stepsCount: number) {
  const nextIndex = currentIndex + 1;
  return nextIndex < stepsCount ? nextIndex : null;
}
