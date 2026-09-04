import { useCallback, useEffect } from "react";
import { useBeforeUnload, useBlocker } from "react-router";

import type { DurableFormStatus } from "./useDurableForm";

export function getDraftGuardState(isActive: boolean, saveStatus: DurableFormStatus) {
  return {
    blockClientNavigation: isActive && (saveStatus === "saving" || saveStatus === "failed"),
    warnBeforeUnload: isActive && (saveStatus === "saving" || saveStatus === "failed"),
  };
}

export function useUnsavedDraftGuard(isActive: boolean, saveStatus: DurableFormStatus) {
  const guard = getDraftGuardState(isActive, saveStatus);
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      guard.blockClientNavigation && (currentLocation.pathname !== nextLocation.pathname || currentLocation.search !== nextLocation.search),
  );

  useBeforeUnload(
    useCallback(
      (event) => {
        if (guard.warnBeforeUnload) {
          event.preventDefault();
        }
      },
      [guard.warnBeforeUnload],
    ),
  );

  useEffect(() => {
    if (blocker.state !== "blocked") {
      return;
    }

    const message =
      saveStatus === "saving"
        ? "Черновик ещё сохраняется. Всё равно покинуть страницу?"
        : "Черновик не удалось сохранить. Покинуть страницу и потерять последние изменения?";
    if (window.confirm(message)) {
      blocker.proceed();
    } else {
      blocker.reset();
    }
  }, [blocker, saveStatus]);
}
