import { beforeEach, describe, expect, it } from "vitest";

import {
  clearReferenceLabels,
  discardLegacyReferenceLabels,
  getCachedReferenceLabel,
  rememberReferenceLabel,
  rememberReferenceLabels,
} from "./referenceLabels";

beforeEach(() => {
  clearReferenceLabels();
  localStorage.clear();
});

describe("reference labels", () => {
  it("keeps transient labels available without writing personal data to Web Storage", () => {
    rememberReferenceLabel("client", "client-1", "Ada Lovelace");
    rememberReferenceLabels("service", [{ id: "service-1", label: "Piano" }]);

    expect(getCachedReferenceLabel("client", "client-1")).toBe("Ada Lovelace");
    expect(getCachedReferenceLabel("service", "service-1")).toBe("Piano");
    expect(localStorage.length).toBe(0);
  });

  it("clears labels on a session boundary", () => {
    rememberReferenceLabel("user", "user-1", "Teacher");
    clearReferenceLabels();
    expect(getCachedReferenceLabel("user", "user-1")).toBeUndefined();
  });

  it("removes only legacy reference-label entries", () => {
    localStorage.setItem("melodytrack:reference-labels:client", "{}");
    localStorage.setItem("melodytrack.theme", "dark");

    discardLegacyReferenceLabels(localStorage);

    expect(localStorage.getItem("melodytrack:reference-labels:client")).toBeNull();
    expect(localStorage.getItem("melodytrack.theme")).toBe("dark");
  });
});
