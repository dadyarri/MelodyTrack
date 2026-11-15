import { afterEach, describe, expect, it } from "vitest";
import { attachPopupScrollBoundary } from "./popupScrollBoundary";

let detach: (() => void) | undefined;
afterEach(() => {
  detach?.();
  detach = undefined;
  document.body.replaceChildren();
});

function popup(scrollTop: number, className = "ant-select-dropdown-list-holder") {
  const root = document.createElement("div");
  root.className = "ant-select-dropdown";
  const holder = document.createElement("div");
  holder.className = className;
  holder.style.overflowY = "hidden";
  Object.defineProperties(holder, {
    clientHeight: { value: 100 },
    scrollHeight: { value: 300 },
  });
  holder.scrollTop = scrollTop;
  root.append(holder);
  document.body.append(root);
  detach = attachPopupScrollBoundary(document);
  return holder;
}

function wheel(target: Element, init: WheelEventInit = {}) {
  const event = new WheelEvent("wheel", { bubbles: true, cancelable: true, deltaY: 40, ...init });
  target.dispatchEvent(event);
  return event;
}

describe("popup scroll boundary", () => {
  it("contains outward wheel movement at both virtual-list boundaries", () => {
    const holder = popup(0);

    const upward = wheel(holder, { deltaY: -40 });
    holder.scrollTop = 200;
    const downward = wheel(holder);

    expect(upward.defaultPrevented).toBe(true);
    expect(downward.defaultPrevented).toBe(true);
  });

  it("preserves movement within a list and back from its boundary", () => {
    const holder = popup(100);

    expect(wheel(holder).defaultPrevented).toBe(false);
    holder.scrollTop = 200;
    expect(wheel(holder, { deltaY: -40 }).defaultPrevented).toBe(false);
  });

  it("allows a nested scrollable popup element to consume the movement", () => {
    const holder = popup(200);
    const nested = document.createElement("div");
    nested.style.overflowY = "auto";
    Object.defineProperties(nested, {
      clientHeight: { value: 20 },
      scrollHeight: { value: 100 },
    });
    holder.append(nested);

    expect(wheel(nested).defaultPrevented).toBe(false);
  });

  it("leaves page scrolling, zoom and horizontal gestures untouched", () => {
    const holder = popup(200);

    expect(wheel(document.body).defaultPrevented).toBe(false);
    expect(wheel(holder, { ctrlKey: true }).defaultPrevented).toBe(false);
    expect(wheel(holder, { shiftKey: true }).defaultPrevented).toBe(false);
    expect(wheel(holder, { deltaX: 80 }).defaultPrevented).toBe(false);
  });

  it("contains dropdown-menu boundaries and releases its listener on cleanup", () => {
    const holder = popup(0);
    holder.parentElement?.classList.replace("ant-select-dropdown", "ant-dropdown");
    holder.className = "ant-dropdown-menu";
    holder.style.overflowY = "auto";
    holder.scrollTop = 200;

    expect(wheel(holder).defaultPrevented).toBe(true);
    detach?.();
    expect(wheel(holder).defaultPrevented).toBe(false);
  });
});
