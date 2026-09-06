# Client portal sessions

`GET /p/{token}` is a backend-owned browser navigation, outside `/api`. A valid permanent link creates a normal session for its linked client user, issues the existing refresh/CSRF cookies, and redirects to `/portal`. The frontend restores its short-lived access token through the existing CSRF-protected refresh endpoint. No credential is passed to React in a URL, response body, or browser storage.

New tokens are 16 cryptographically random bytes encoded as URL-safe base64 (128 bits). The database stores only the SHA-256 token hash with a unique index. The cutover deletes all old portal links, so changing an old link's path to `/p/` cannot authenticate a client. Newly issued links have no automatic expiry. Invalid, revoked, malformed, and rate-limited navigations redirect to `/portal/error` with an actionable token-free message. The retired `/portal/access/*` path also redirects there; it no longer authenticates clients.

The same link can be opened repeatedly or on multiple devices. Each entry creates a session with the normal 30-day sliding portal lifetime. Opening another client's link replaces the browser's refresh cookie. There is one renewable identity per browser profile, no saved-profile chooser, and no PIN. Old saved-profile metadata is removed on application startup. Logging out revokes the current refresh session; it does not invalidate the link or other devices.

Administrators use **Новая ссылка** in client history to create or regenerate a link. Regeneration invalidates the previous hash and revokes all of that client's active sessions in one transaction. Revocation disables the link and revokes the same sessions. Entry and refresh lock the link row alongside regeneration/revocation so a request using the retired link or session cannot create a new active session after either operation. God mode retains link regeneration/revocation and removes PIN reset.

The portal route tree and minimally styled shell live separately from the administrative shell. `/portal` currently opens the existing schedule, with vacations available in the same portal navigation. Player aliases, RPG visuals, and the redesigned home remain deferred.

## Hosting and rollout

Vite proxies `/p` and retired `/portal/access` navigations to Backend as well as the existing `/api` traffic. Kestrel owns the corresponding routes in production. Credential navigations and generated-link responses use `no-store`; navigation uses `Referrer-Policy: no-referrer`. Credential paths are excluded from application request logs and HTTP tracing. Configure the external reverse proxy to omit or redact `/p/*` and retired credential paths in its own access/error logs too.

The migration removes PIN hashes, setup/cooldown fields, saved-reference records, and all existing portal links. It force-revokes every client session, including expired sessions not already revoked; staff sessions and client user accounts are unchanged. Administrators must issue and send new personal links after upgrade. Historical audit event labels and old migrations remain for history. Remove `AuthenticationSecrets__PortalPinPepper` from deployment configuration. No replacement secret is required. Apply the migration only through the normal backed-up deployment/Init workflow; rolling the schema back cannot restore removed links, PINs, or saved references and does not reactivate revoked sessions.

Client users are permanently ineligible for onboarding. Every onboarding endpoint returns `403 Forbidden` before loading onboarding state, so no new client onboarding row can be created. Historical client onboarding rows are retained because deleting old state provides no runtime benefit and would discard potentially useful audit history; they are inert and are not migrated or updated.

## Next appointment

The portal's next appointment is the earliest appointment owned by the signed-in
client that is not deleted, has `planned` status, and whose end time is at or
after the server's current UTC time. This deliberately keeps an appointment in
progress visible, but excludes completed, cancelled, burned, and already-ended
appointments. The endpoint returns this as `nextAppointment`, or `null` when
there is no eligible appointment; it is not a schedule list.
