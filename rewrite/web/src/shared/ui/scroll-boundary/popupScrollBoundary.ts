const popupSelector = "[data-popup-scroll-boundary]";

export function attachPopupScrollBoundary(document: Document): () => void {
  const onWheel = (event: WheelEvent) => {
    if (
      event.defaultPrevented ||
      !event.cancelable ||
      event.ctrlKey ||
      event.shiftKey ||
      event.deltaY === 0 ||
      Math.abs(event.deltaX) > Math.abs(event.deltaY)
    )
      return;

    const target = event.target instanceof Element ? event.target : null;
    const popup = target?.closest(popupSelector);
    if (!target || !popup) return;

    for (let element: Element | null = target; element; element = element.parentElement) {
      const overflow = window.getComputedStyle(element).overflowY;
      const scrollable = overflow === "auto" || overflow === "scroll";
      const remaining = element.scrollHeight - element.clientHeight - element.scrollTop;
      if (scrollable && (event.deltaY < 0 ? element.scrollTop > 0 : remaining > 1)) return;
      if (element === popup) break;
    }

    // Keep wheel gestures at the popup boundary from scrolling the background.
    event.preventDefault();
  };

  document.addEventListener("wheel", onWheel, { passive: false });
  return () => document.removeEventListener("wheel", onWheel);
}
