import { describe, expect, it } from "vitest";

import { setVisualViewportCssVariables } from "./useVisualViewportCssVariables";

describe("visual viewport CSS variables", () => {
  it("publishes keyboard-sensitive viewport dimensions and offsets", () => {
    const root = document.createElement("div");

    setVisualViewportCssVariables(root, {
      height: 356.5,
      width: 320,
      offsetLeft: 0,
      offsetTop: 142,
    });

    expect(root.style.getPropertyValue("--visual-viewport-height")).toBe("356.5px");
    expect(root.style.getPropertyValue("--visual-viewport-width")).toBe("320px");
    expect(root.style.getPropertyValue("--visual-viewport-offset-left")).toBe("0px");
    expect(root.style.getPropertyValue("--visual-viewport-offset-top")).toBe("142px");
  });
});
