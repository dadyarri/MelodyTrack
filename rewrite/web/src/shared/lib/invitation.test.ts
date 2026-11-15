import { afterEach, describe, expect, it } from "vitest";
import { takeInvitationFromAddress } from "./invitation";

afterEach(() => window.history.replaceState(null, "", "/"));

describe("invitation address", () => {
  it("takes the invitation once and removes it from the address before rendering", () => {
    const invitation = "A".repeat(64);
    window.history.replaceState(null, "", `/auth/register#invitation=${invitation}`);

    const result = takeInvitationFromAddress();

    expect(result).toBe(invitation);
    expect(window.location.hash).toBe("");
    expect(window.location.pathname).toBe("/auth/register");
    expect(takeInvitationFromAddress()).toBeNull();
  });

  it("rejects ambiguous proofs and removes all fragment content", () => {
    window.history.replaceState(
      null,
      "",
      `/auth/register#invitation=${"A".repeat(64)}&invitation=${"B".repeat(64)}`,
    );

    expect(takeInvitationFromAddress()).toBeNull();
    expect(window.location.hash).toBe("");
  });

  it("leaves unrelated page anchors alone", () => {
    window.history.replaceState(null, "", "/learning#article");

    expect(takeInvitationFromAddress()).toBeNull();
    expect(window.location.hash).toBe("#article");
  });
});
