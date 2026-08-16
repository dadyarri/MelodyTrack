const excludedRoutePrefixes = ["/login", "/restore", "/invite", "/portal"];

export function isReleaseNotesEligiblePath(pathname: string) {
  return !excludedRoutePrefixes.some((prefix) => pathname === prefix || pathname.startsWith(`${prefix}/`));
}
