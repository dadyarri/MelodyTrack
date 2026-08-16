import { useEffect } from "react";

const compactViewportQuery = "(max-width: 720px)";
const overlayInputSelector = ".ant-picker input, .ant-select-input";

type OriginalInputState = {
  inputMode: string;
  readOnly: boolean;
};

export function useMobileOverlayKeyboardPolicy() {
  useEffect(() => {
    const mediaQuery = window.matchMedia(compactViewportQuery);
    const originalStates = new Map<HTMLInputElement, OriginalInputState>();

    const prepareInput = (input: HTMLInputElement) => {
      if (!originalStates.has(input)) {
        originalStates.set(input, { inputMode: input.inputMode, readOnly: input.readOnly });
      }

      input.readOnly = true;
      input.inputMode = "none";
    };

    const restoreInputs = () => {
      for (const [input, originalState] of originalStates) {
        input.readOnly = originalState.readOnly;
        input.inputMode = originalState.inputMode;
      }
      originalStates.clear();
    };

    const syncInputs = () => {
      if (!mediaQuery.matches) {
        restoreInputs();
        return;
      }

      document.querySelectorAll<HTMLInputElement>(overlayInputSelector).forEach(prepareInput);
      for (const input of originalStates.keys()) {
        if (!input.isConnected) {
          originalStates.delete(input);
        }
      }
    };

    const handlePointerDown = (event: PointerEvent) => {
      if (!mediaQuery.matches || !(event.target instanceof Element)) {
        return;
      }

      const control = event.target.closest<HTMLElement>(".ant-picker, .ant-select");
      if (!control) {
        return;
      }

      control.querySelectorAll<HTMLInputElement>("input").forEach(prepareInput);
      const activeElement = document.activeElement;
      if (activeElement instanceof HTMLInputElement || activeElement instanceof HTMLTextAreaElement) {
        activeElement.blur();
      }
    };

    const observer = new MutationObserver(syncInputs);
    observer.observe(document.body, { childList: true, subtree: true });
    mediaQuery.addEventListener("change", syncInputs);
    document.addEventListener("pointerdown", handlePointerDown, true);
    syncInputs();

    return () => {
      observer.disconnect();
      mediaQuery.removeEventListener("change", syncInputs);
      document.removeEventListener("pointerdown", handlePointerDown, true);
      restoreInputs();
    };
  }, []);
}
