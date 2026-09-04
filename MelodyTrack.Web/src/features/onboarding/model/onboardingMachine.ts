export type OnboardingRetryAction = "progress" | "complete" | "skip";
export type OnboardingPhase = "idle" | "navigating" | "locating" | "ready" | "saving" | "finishing" | "failed";

export type OnboardingMachineState = {
  phase: OnboardingPhase;
  requestId: number;
  retryAction?: OnboardingRetryAction;
};

export type OnboardingMachineEvent =
  | { type: "close" }
  | { type: "route-required" }
  | { type: "target-required" }
  | { type: "target-found" }
  | { type: "operation-started"; requestId: number; action: OnboardingRetryAction }
  | { type: "operation-succeeded"; requestId: number; closesTour?: boolean }
  | { type: "operation-failed"; requestId: number; action: OnboardingRetryAction }
  | { type: "retry" };

export const initialOnboardingMachineState: OnboardingMachineState = {
  phase: "idle",
  requestId: 0,
};

export function reduceOnboardingMachine(state: OnboardingMachineState, event: OnboardingMachineEvent): OnboardingMachineState {
  switch (event.type) {
    case "close":
      return initialOnboardingMachineState;
    case "route-required":
      return { ...state, phase: "navigating", retryAction: undefined };
    case "target-required":
      return { ...state, phase: "locating", retryAction: undefined };
    case "target-found":
      return { ...state, phase: "ready", retryAction: undefined };
    case "operation-started":
      return {
        phase: event.action === "progress" ? "saving" : "finishing",
        requestId: event.requestId,
        retryAction: event.action,
      };
    case "operation-succeeded":
      if (event.requestId !== state.requestId) {
        return state;
      }
      return event.closesTour ? initialOnboardingMachineState : { ...state, phase: "ready", retryAction: undefined };
    case "operation-failed":
      if (event.requestId !== state.requestId) {
        return state;
      }
      return { ...state, phase: "failed", retryAction: event.action };
    case "retry":
      return state.retryAction ? { ...state, phase: state.retryAction === "progress" ? "saving" : "finishing" } : state;
  }
}

export function isLatestOnboardingRequest(requestId: number, latestRequestId: number) {
  return requestId === latestRequestId;
}

export function canShowOnboardingStep({
  phase,
  currentPath,
  stepPath,
  hasTarget,
}: {
  phase: OnboardingPhase;
  currentPath: string;
  stepPath: string;
  hasTarget: boolean;
}) {
  return (phase === "ready" || phase === "failed") && currentPath === stepPath && hasTarget;
}
