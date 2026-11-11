import type {
  CurrentStaffResponse,
  StaffCredentialResponse,
  StaffPermissionResponse,
} from "./generated/models";
import { invalidResponse, isObject } from "./http";

type PresentFields<T, K extends keyof T> = { [P in K]-?: NonNullable<T[P]> };

export type StaffPermission = PresentFields<
  StaffPermissionResponse,
  "permission" | "allowed" | "dataScope" | "timeScope"
>;

export type StaffIdentity = PresentFields<CurrentStaffResponse, "staffId" | "login"> & {
  role: "superuser" | "administrator" | "teacher";
  permissions: StaffPermission[];
};

export type StaffTokens = PresentFields<
  StaffCredentialResponse,
  "accessToken" | "refreshToken" | "staffId" | "expiresIn"
> & {
  kind: "tokens";
};

export type StaffFactor = {
  kind: "factor";
  challenge: string;
  method: "authenticator" | "authenticatorOrPasskey";
  optionsJson: string | null;
};

export type PasskeyChallenge = PresentFields<StaffCredentialResponse, "challenge" | "optionsJson">;
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
    !nonempty(value.login) ||
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
  return { staffId: value.staffId, login: value.login, role: value.role as StaffIdentity["role"], permissions };
}
