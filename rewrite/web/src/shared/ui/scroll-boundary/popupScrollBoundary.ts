const popupSelector =
  ".ant-select-dropdown, .ant-tree-select-dropdown, .ant-dropdown, .ant-cascader-dropdown";
const virtualHolderSelector =
  ".ant-select-dropdown-list-holder, .ant-select-tree-list-holder, .rc-virtual-list-holder";

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
      const scrollable =
        overflow === "auto" ||
        overflow === "scroll" ||
        (overflow === "hidden" && element.matches(virtualHolderSelector));
      const remaining = element.scrollHeight - element.clientHeight - element.scrollTop;
      if (scrollable && (event.deltaY < 0 ? element.scrollTop > 0 : remaining > 1)) return;
      if (element === popup) break;
    }

    // Virtual lists release wheel events at their limits despite overflow: hidden.
    event.preventDefault();
  };

  document.addEventListener("wheel", onWheel, { passive: false });
  return () => document.removeEventListener("wheel", onWheel);
}
