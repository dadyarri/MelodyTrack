import { useEffect } from "react";

type ViewportMetrics = Pick<VisualViewport, "height" | "width" | "offsetLeft" | "offsetTop">;

export function setVisualViewportCssVariables(root: HTMLElement, viewport: ViewportMetrics) {
  root.style.setProperty("--visual-viewport-height", `${String(Math.max(0, viewport.height))}px`);
  root.style.setProperty("--visual-viewport-width", `${String(Math.max(0, viewport.width))}px`);
  root.style.setProperty("--visual-viewport-offset-left", `${String(Math.max(0, viewport.offsetLeft))}px`);
  root.style.setProperty("--visual-viewport-offset-top", `${String(Math.max(0, viewport.offsetTop))}px`);
}

export function useVisualViewportCssVariables() {
  useEffect(() => {
    const root = document.documentElement;
    const visualViewport = window.visualViewport;
    const syncViewport = () => {
      setVisualViewportCssVariables(
        root,
        visualViewport ?? {
          height: window.innerHeight,
          width: window.innerWidth,
          offsetLeft: 0,
          offsetTop: 0,
        },
      );
    };

    syncViewport();
    window.addEventListener("resize", syncViewport);
    window.addEventListener("orientationchange", syncViewport);
    visualViewport?.addEventListener("resize", syncViewport);
    visualViewport?.addEventListener("scroll", syncViewport);

    return () => {
      window.removeEventListener("resize", syncViewport);
      window.removeEventListener("orientationchange", syncViewport);
      visualViewport?.removeEventListener("resize", syncViewport);
      visualViewport?.removeEventListener("scroll", syncViewport);
    };
  }, []);
}
