import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useMemo, useReducer, useRef } from "react";
import { useLocation, useNavigate } from "react-router";

import { useAuth } from "@/entities/session";

import { onboardingApi, type OnboardingStateResponse } from "../api/onboardingApi";
import { onboardingQueryKeys } from "../api/queryKeys";
import { getOnboardingJourney, getOnboardingStepIndex } from "./journeys";
import {
  canShowOnboardingStep,
  initialOnboardingMachineState,
  isLatestOnboardingRequest,
  type OnboardingRetryAction,
  reduceOnboardingMachine,
} from "./onboardingMachine";
import { findOnboardingTarget, getMissingTargetRecoveryIndex } from "./targets";
import { ONBOARDING_DEFINITION_VERSION } from "./types";

const targetWaitMs = 2_000;

export function useOnboardingController({ onCompleted }: { onCompleted?: () => void } = {}) {
  const auth = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const latestRequestIdRef = useRef(0);
  const [machine, dispatch] = useReducer(reduceOnboardingMachine, initialOnboardingMachineState);

  const onboardingQuery = useQuery({
    queryKey: onboardingQueryKeys.state,
    queryFn: () => onboardingApi.getState(),
    enabled: auth.isAuthenticated && !auth.user?.isClientPortal,
  });
  const journey = useMemo(() => getOnboardingJourney(auth.user), [auth.user]);
  const steps = journey.steps;
  const currentState = onboardingQuery.data;
  const currentStepIndex = useMemo(() => getOnboardingStepIndex(journey, currentState?.currentStep), [currentState?.currentStep, journey]);
  const currentStep = steps[currentStepIndex];

  const setState = useCallback(
    (state: OnboardingStateResponse) => {
      queryClient.setQueryData(onboardingQueryKeys.state, state);
    },
    [queryClient],
  );

  const beginOperation = useCallback((action: OnboardingRetryAction) => {
    const requestId = latestRequestIdRef.current + 1;
    latestRequestIdRef.current = requestId;
    dispatch({ type: "operation-started", action, requestId });
    return requestId;
  }, []);

  const { isPending: isProgressPending, mutate: mutateProgress } = useMutation({
    mutationFn: async (input: { currentStep: string; currentPath: string; requestId: number }) => {
      const state = await onboardingApi.updateProgress({
        currentStep: input.currentStep,
        currentPath: input.currentPath,
      });
      return { state, requestId: input.requestId };
    },
    onSuccess: ({ state, requestId }) => {
      if (!isLatestOnboardingRequest(requestId, latestRequestIdRef.current)) {
        return;
      }
      setState(state);
      dispatch({ type: "operation-succeeded", requestId });
    },
    onError: (_error, input) => {
      if (isLatestOnboardingRequest(input.requestId, latestRequestIdRef.current)) {
        dispatch({ type: "operation-failed", action: "progress", requestId: input.requestId });
      }
    },
  });

  const { isPending: isCompletePending, mutate: mutateComplete } = useMutation({
    mutationFn: async (input: { requestId: number }) => ({ state: await onboardingApi.complete(), requestId: input.requestId }),
    onSuccess: ({ state, requestId }) => {
      if (!isLatestOnboardingRequest(requestId, latestRequestIdRef.current)) {
        return;
      }
      setState(state);
      dispatch({ type: "operation-succeeded", requestId, closesTour: true });
      onCompleted?.();
    },
    onError: (_error, input) => {
      if (isLatestOnboardingRequest(input.requestId, latestRequestIdRef.current)) {
        dispatch({ type: "operation-failed", action: "complete", requestId: input.requestId });
      }
    },
  });

  const { isPending: isSkipPending, mutate: mutateSkip } = useMutation({
    mutationFn: async (input: { requestId: number }) => ({ state: await onboardingApi.skip(), requestId: input.requestId }),
    onSuccess: ({ state, requestId }) => {
      if (!isLatestOnboardingRequest(requestId, latestRequestIdRef.current)) {
        return;
      }
      setState(state);
      dispatch({ type: "operation-succeeded", requestId, closesTour: true });
    },
    onError: (_error, input) => {
      if (isLatestOnboardingRequest(input.requestId, latestRequestIdRef.current)) {
        dispatch({ type: "operation-failed", action: "skip", requestId: input.requestId });
      }
    },
  });

  const saveStep = useCallback(
    (nextIndex: number) => {
      const nextStep = steps.at(nextIndex);
      if (!nextStep || !currentState || isProgressPending) {
        return;
      }

      const requestId = beginOperation("progress");
      setState({
        ...currentState,
        status: "active",
        currentStep: nextStep.id,
        currentPath: nextStep.path,
        definitionVersion: ONBOARDING_DEFINITION_VERSION,
        shouldLaunch: true,
        updatedAtUtc: new Date().toISOString(),
        completedAtUtc: null,
      });
      mutateProgress({ currentStep: nextStep.id, currentPath: nextStep.path, requestId });
      if (location.pathname !== nextStep.path) {
        void navigate(nextStep.path);
      }
    },
    [beginOperation, currentState, isProgressPending, location.pathname, mutateProgress, navigate, setState, steps],
  );

  const complete = useCallback(() => {
    if (isCompletePending) {
      return;
    }
    mutateComplete({ requestId: beginOperation("complete") });
  }, [beginOperation, isCompletePending, mutateComplete]);

  const skip = useCallback(() => {
    if (isSkipPending) {
      return;
    }
    mutateSkip({ requestId: beginOperation("skip") });
  }, [beginOperation, isSkipPending, mutateSkip]);

  const retry = useCallback(() => {
    const retryAction = machine.retryAction;
    if (!retryAction) {
      return;
    }
    dispatch({ type: "retry" });
    if (retryAction === "complete") {
      complete();
    } else if (retryAction === "skip") {
      skip();
    } else {
      saveStep(currentStepIndex);
    }
  }, [complete, currentStepIndex, machine.retryAction, saveStep, skip]);

  useEffect(() => {
    if (!currentState?.shouldLaunch) {
      dispatch({ type: "close" });
      return;
    }

    if (location.pathname !== currentStep.path) {
      dispatch({ type: "route-required" });
      void navigate(currentStep.path);
      return;
    }

    dispatch({ type: currentStep.targetId ? "target-required" : "target-found" });
  }, [currentState?.shouldLaunch, currentStep, location.pathname, navigate]);

  useEffect(() => {
    const targetId = currentStep.targetId;
    if (machine.phase !== "locating" || !targetId) {
      return;
    }

    let cancelled = false;
    let timeoutId: number | undefined;
    const startedAt = Date.now();
    const locate = () => {
      if (cancelled) {
        return;
      }
      const target = findOnboardingTarget(targetId);
      if (target) {
        target.scrollIntoView({ block: "center", inline: "nearest" });
        dispatch({ type: "target-found" });
        return;
      }
      if (Date.now() - startedAt >= targetWaitMs) {
        const nextIndex = getMissingTargetRecoveryIndex(currentStepIndex, steps.length);
        if (nextIndex !== null) {
          saveStep(nextIndex);
        } else {
          complete();
        }
        return;
      }
      timeoutId = window.setTimeout(locate, 50);
    };

    locate();
    return () => {
      cancelled = true;
      if (timeoutId !== undefined) {
        window.clearTimeout(timeoutId);
      }
    };
  }, [complete, currentStep, currentStepIndex, machine.phase, saveStep, steps.length]);

  const isBusy = isProgressPending || isCompletePending || isSkipPending;
  const hasTarget = !currentStep.targetId || findOnboardingTarget(currentStep.targetId) !== null;
  const canShowStep = canShowOnboardingStep({
    phase: machine.phase,
    currentPath: location.pathname,
    stepPath: currentStep.path,
    hasTarget,
  });

  return {
    steps,
    currentStepIndex,
    open: Boolean(currentState?.shouldLaunch && canShowStep),
    isLoading: onboardingQuery.isPending,
    isActive: Boolean(currentState?.shouldLaunch),
    isBusy,
    hasError: machine.phase === "failed",
    changeStep: saveStep,
    complete,
    skip,
    retry,
  };
}

export type OnboardingController = ReturnType<typeof useOnboardingController>;
