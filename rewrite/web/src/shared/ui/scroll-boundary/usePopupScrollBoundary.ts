import { useEffect } from "react";
import { attachPopupScrollBoundary } from "./popupScrollBoundary";

export function usePopupScrollBoundary() {
  useEffect(() => attachPopupScrollBoundary(document), []);
}
