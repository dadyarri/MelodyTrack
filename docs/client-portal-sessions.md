# Client portal saved sessions

The browser may remember a client portal identity after a successful access-link login. The saved record contains only a privacy-conscious display label, the client identity ID, the last-used time, and an opaque random reference. The invitation URL, PIN, access token, and refresh token are never returned as chooser metadata.

The server stores only a SHA-256 hash of each opaque reference. A reference identifies an active portal link but cannot create a session without the client's PIN. Link rotation, link revocation, and PIN reset delete every reference for that link and revoke active sessions. A stale browser entry therefore fails closed and can be removed locally.

Logging out revokes only the active refresh session. Forgetting a chooser entry is a browser-local privacy action and does not revoke sessions on other browsers.

Client users are permanently ineligible for onboarding. Every onboarding endpoint returns `403 Forbidden` before loading onboarding state, so no new client onboarding row can be created. Historical client onboarding rows are retained because deleting old state provides no runtime benefit and would discard potentially useful audit history; they are inert and are not migrated or updated.

## Next appointment

The portal's next appointment is the earliest appointment owned by the signed-in
client that is not deleted, has `planned` status, and whose end time is at or
after the server's current UTC time. This deliberately keeps an appointment in
progress visible, but excludes completed, cancelled, burned, and already-ended
appointments. The endpoint returns this as `nextAppointment`, or `null` when
there is no eligible appointment; it is not a schedule list.
