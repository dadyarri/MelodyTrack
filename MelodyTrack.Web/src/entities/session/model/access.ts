import type { MeResponse } from "../api/sessionApi";

export type AppUser = MeResponse | null | undefined;
export type AccessAudience = "all" | "admin" | "superuser" | "stats";

export function hasAdminAccess(user: AppUser) {
  return Boolean(user?.isAdmin);
}

export function hasSuperuserAccess(user: AppUser) {
  return Boolean(user?.isSuperuser);
}

export function hasClientPortalAccess(user: AppUser) {
  return Boolean(user?.isClientPortal);
}

export function hasStatsAccess(user: AppUser) {
  return hasAdminAccess(user) || hasSuperuserAccess(user);
}

export function canAccessAudience(user: AppUser, audience: AccessAudience = "all") {
  switch (audience) {
    case "admin":
      return hasAdminAccess(user);
    case "superuser":
      return hasSuperuserAccess(user);
    case "stats":
      return hasStatsAccess(user);
    default:
      return true;
  }
}
