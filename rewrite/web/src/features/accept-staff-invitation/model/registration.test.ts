import { describe, expect, it } from "vitest";
import {
  authenticatorUri,
  canSkipAuthenticator,
  canSkipPasskey,
  readAuthenticatorSetup,
  readPasskeySetup,
} from "./registration";

describe("invitation registration steps", () => {
  it("keeps an independent factor mandatory for a superuser", () => {
    expect(canSkipAuthenticator("superuser", false)).toBe(false);
    expect(canSkipAuthenticator("superuser", true)).toBe(true);
    expect(canSkipPasskey("superuser", true, false)).toBe(false);
    expect(canSkipPasskey("superuser", true, true)).toBe(true);
    expect(canSkipPasskey("superuser", false, true)).toBe(false);
  });

  it("allows a teacher to skip the authenticator but requires a primary credential", () => {
    expect(canSkipAuthenticator("teacher", false)).toBe(true);
    expect(canSkipPasskey("teacher", true, false)).toBe(true);
    expect(canSkipPasskey("teacher", false, false)).toBe(false);
  });

  it("rejects incomplete step responses", () => {
    expect(() => readAuthenticatorSetup({ challenge: "test" })).toThrow();
    expect(() => readPasskeySetup({ challenge: "test" })).toThrow();
  });

  it("does not retain an authenticator secret in the passkey step", () => {
    expect(
      readPasskeySetup({
        challenge: "next-test",
        optionsJson: "{}",
        authenticatorSecret: "SYNTHETICKEY",
      }),
    ).toEqual({ challenge: "next-test", optionsJson: "{}" });
  });

  it("encodes the account label without changing TOTP parameters", () => {
    const uri = new URL(authenticatorUri("staff / demo", "SYNTHETICKEY"));

    expect(decodeURIComponent(uri.pathname)).toBe("/MelodyTrack:staff / demo");
    expect(uri.searchParams.get("secret")).toBe("SYNTHETICKEY");
    expect(uri.searchParams.get("issuer")).toBe("MelodyTrack");
    expect(uri.searchParams.get("digits")).toBe("6");
    expect(uri.searchParams.get("period")).toBe("30");
  });
});
