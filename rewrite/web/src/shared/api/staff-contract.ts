import { invalidResponse, isObject } from "./http";

export type StaffPermission = {
  permission: string;
  allowed: boolean;
  dataScope: string;
  timeScope: string;
};

export type StaffIdentity = {
  staffId: string;
  role: "superuser" | "administrator" | "teacher";
  permissions: StaffPermission[];
};

export type StaffTokens = {
  kind: "tokens";
  accessToken: string;
  refreshToken: string;
  staffId: string;
  expiresIn: number;
};

export type StaffFactor = {
  kind: "factor";
  challenge: string;
  method: "authenticator" | "authenticatorOrPasskey";
  optionsJson: string | null;
};

export type PasskeyChallenge = { challenge: string; optionsJson: string };
export type StaffCredentialOutcome = StaffTokens | StaffFactor;

function nonempty(value: unknown): value is string {
  return typeof value === "string" && value.length > 0;
}

export function readStaffOutcome(value: unknown): StaffCredentialOutcome {
  if (!isObject(value)) throw invalidResponse();
  if (
    nonempty(value.accessToken) &&
    nonempty(value.refreshToken) &&
    nonempty(value.staffId) &&
    typeof value.expiresIn === "number" &&
    Number.isFinite(value.expiresIn) &&
    value.expiresIn > 0
  ) {
    return {
      kind: "tokens",
      accessToken: value.accessToken,
      refreshToken: value.refreshToken,
      staffId: value.staffId,
      expiresIn: value.expiresIn,
    };
  }
  if (
    nonempty(value.challenge) &&
    (value.factor === "authenticator" || value.factor === "authenticatorOrPasskey") &&
    (value.optionsJson === null ||
      value.optionsJson === undefined ||
      typeof value.optionsJson === "string")
  ) {
    return {
      kind: "factor",
      challenge: value.challenge,
      method: value.factor,
      optionsJson: value.optionsJson ?? null,
    };
  }
  throw invalidResponse();
}

export function readPasskeyChallenge(value: unknown): PasskeyChallenge {
  if (!isObject(value) || !nonempty(value.challenge) || !nonempty(value.optionsJson))
    throw invalidResponse();
  return { challenge: value.challenge, optionsJson: value.optionsJson };
}

export function readStaffIdentity(value: unknown): StaffIdentity {
  if (
    !isObject(value) ||
    !nonempty(value.staffId) ||
    !["superuser", "administrator", "teacher"].includes(String(value.role)) ||
    !Array.isArray(value.permissions)
  )
    throw invalidResponse();
  const permissions = value.permissions.map((entry: unknown): StaffPermission => {
    if (
      !isObject(entry) ||
      !nonempty(entry.permission) ||
      typeof entry.allowed !== "boolean" ||
      !nonempty(entry.dataScope) ||
      !nonempty(entry.timeScope)
    )
      throw invalidResponse();
    return {
      permission: entry.permission,
      allowed: entry.allowed,
      dataScope: entry.dataScope,
      timeScope: entry.timeScope,
    };
  });
  return { staffId: value.staffId, role: value.role as StaffIdentity["role"], permissions };
}
