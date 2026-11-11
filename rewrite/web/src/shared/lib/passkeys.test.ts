import { afterEach, describe, expect, it, vi } from "vitest";
import { createPasskeyRegistration } from "./passkeys";

const options = JSON.stringify({
  challenge: "AQID",
  rp: { id: "localhost", name: "MelodyTrack" },
  user: { id: "BAUG", name: "staff.demo", displayName: "staff.demo" },
  pubKeyCredParams: [{ type: "public-key", alg: -7 }],
  authenticatorSelection: { userVerification: "required", residentKey: "preferred" },
  excludeCredentials: [{ type: "public-key", id: "BwgJ" }],
  attestation: "none",
});

afterEach(() => vi.unstubAllGlobals());

describe("passkey registration", () => {
  it("calls WebAuthn immediately with decoded options and serializes its response", async () => {
    class Attestation {
      attestationObject = new Uint8Array([1, 2, 3]).buffer;
      clientDataJSON = new Uint8Array([4, 5, 6]).buffer;
      getTransports() { return ["internal"]; }
    }
    class Credential {
      response = new Attestation();
      rawId = new Uint8Array([7, 8, 9]).buffer;
      id = "BwgJ";
      type = "public-key";
      getClientExtensionResults() { return {}; }
    }
    const create = vi.fn().mockResolvedValue(new Credential());
    vi.stubGlobal("isSecureContext", true);
    vi.stubGlobal("PublicKeyCredential", Credential);
    vi.stubGlobal("AuthenticatorAttestationResponse", Attestation);
    vi.stubGlobal("navigator", { credentials: { get: vi.fn(), create } });
    const signal = new AbortController().signal;

    const pending = createPasskeyRegistration(options, signal);

    expect(create).toHaveBeenCalledOnce();
    const input = create.mock.calls[0]?.[0] as CredentialCreationOptions;
    expect(input.signal).toBe(signal);
    expect(Array.from(new Uint8Array(input.publicKey?.challenge as ArrayBuffer))).toEqual([1, 2, 3]);
    expect(input.publicKey?.authenticatorSelection?.userVerification).toBe("required");
    expect(await pending).toEqual({ id: "BwgJ", rawId: "BwgJ", type: "public-key", response: { attestationObject: "AQID", clientDataJSON: "BAUG", transports: ["internal"] }, clientExtensionResults: {} });
  });

  it("rejects unknown verification settings before calling WebAuthn", async () => {
    const create = vi.fn();
    vi.stubGlobal("isSecureContext", true);
    vi.stubGlobal("PublicKeyCredential", class {});
    vi.stubGlobal("navigator", { credentials: { get: vi.fn(), create } });
    const malformed = JSON.stringify({ ...JSON.parse(options), authenticatorSelection: { userVerification: "unknown" } });

    await expect(createPasskeyRegistration(malformed, new AbortController().signal)).rejects.toThrow();

    expect(create).not.toHaveBeenCalled();
  });
});
