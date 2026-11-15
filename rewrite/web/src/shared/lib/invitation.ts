export function takeInvitationFromAddress(): string | null {
  if (window.location.pathname !== "/auth/register") return null;
  const fragment = new URLSearchParams(window.location.hash.slice(1));
  const invitations = fragment.getAll("invitation");
  // Remove the proof before React, diagnostics, navigation, or browser history can reuse it.
  if (window.location.hash) {
    window.history.replaceState(
      window.history.state,
      "",
      window.location.pathname + window.location.search,
    );
  }
  return invitations.length === 1 && /^[0-9A-F]{64}$/.test(invitations[0] ?? "")
    ? (invitations[0] ?? null)
    : null;
}
