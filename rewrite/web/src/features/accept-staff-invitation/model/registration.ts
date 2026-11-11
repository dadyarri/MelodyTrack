import { invalidResponse, type StaffIdentity } from "@/shared/api";
import type { StaffCredentialResponse } from "@/shared/api/generated/models";

export function canSkipAuthenticator(role: StaffIdentity["role"], hasPassword: boolean): boolean {
  return role === "teacher" || hasPassword;
}

export function canSkipPasskey(role: StaffIdentity["role"], hasPassword: boolean, authenticatorConfirmed: boolean): boolean {
  return hasPassword && (role === "teacher" || authenticatorConfirmed);
}

export function readAuthenticatorSetup(response: StaffCredentialResponse | undefined) {
  if (!response?.challenge || !response.authenticatorSecret) throw invalidResponse();
  return {
    challenge: response.challenge,
    authenticatorSecret: response.authenticatorSecret,
  };
}

export function readPasskeySetup(response: StaffCredentialResponse | undefined) {
  if (!response?.challenge || !response.optionsJson) throw invalidResponse();
  return { challenge: response.challenge, optionsJson: response.optionsJson };
}

export function authenticatorUri(login: string, secret: string): string {
  const query = new URLSearchParams({ secret, issuer: "MelodyTrack", algorithm: "SHA1", digits: "6", period: "30" });
  return `otpauth://totp/${encodeURIComponent(`MelodyTrack:${login}`)}?${query}`;
}
