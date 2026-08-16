import { type AppUser, hasAdminAccess, hasSuperuserAccess } from "@/entities/session";

export function canViewReleaseNotes(user: AppUser) {
  return hasAdminAccess(user) || hasSuperuserAccess(user);
}
