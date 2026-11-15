import { describe, expect, it } from "vitest";
import { ApiError } from "./http";
import { readPasskeyChallenge, readStaffIdentity, readStaffOutcome } from "./staff-contract";

describe("staff credential contract", () => {
  it("keeps a pending factor separate from authenticated access", () => {
    const outcome = readStaffOutcome({
      challenge: "pending",
      factor: "authenticatorOrPasskey",
      optionsJson: null,
      accessToken: null,
    });

    expect(outcome).toEqual({
      kind: "factor",
      challenge: "pending",
      method: "authenticatorOrPasskey",
      optionsJson: null,
    });
    expect("accessToken" in outcome).toBe(false);
  });

  it("rejects partial tokens instead of treating them as a session", () => {
    expect(() =>
      readStaffOutcome({ accessToken: "incomplete", staffId: "staff", expiresIn: 300 }),
    ).toThrow(ApiError);
  });

  it("requires a server challenge together with passkey options", () => {
    expect(() => readPasskeyChallenge({ optionsJson: "{}" })).toThrow(ApiError);
  });

  it("rejects an unknown staff role before rendering staff screens", () => {
    expect(() =>
      readStaffIdentity({ staffId: "id", login: "staff.demo", role: "client", permissions: [] }),
    ).toThrow(ApiError);
  });

  it("preserves effective permission scopes from the server", () => {
    const grant = { permission: "scheduleRead", allowed: true, dataScope: "own", timeScope: "all" };

    const staff = readStaffIdentity({
      staffId: "staff",
      login: "staff.demo",
      role: "teacher",
      permissions: [grant],
    });

    expect(staff.permissions).toEqual([grant]);
    expect(staff.login).toBe("staff.demo");
  });

  it("rejects an identity without a server account name", () => {
    expect(() => readStaffIdentity({ staffId: "staff", role: "teacher", permissions: [] })).toThrow(
      ApiError,
    );
  });
});
