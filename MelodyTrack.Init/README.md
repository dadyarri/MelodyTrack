# MelodyTrack.Init

Run database initialization before starting the backend:

```text
dotnet MelodyTrack.Init.dll --mode production
dotnet MelodyTrack.Init.dll --mode development
dotnet MelodyTrack.Init.dll --mode test
```

Configuration uses standard .NET keys, including `Database__ConnectionString`, `PublicUrl__BaseUrl`, `AuthenticationSecrets__JwtSigningKey`, `PersonalData__CurrentKeyVersion`, and `PersonalData__CurrentKey`. Existing `MELODY_TRACK_*` production variables are mapped during the transition, except the removed separate API base URL.

Development mode creates an idempotent representative data set and the deterministic non-production staff identity below. It preserves the original local generator's behavior within a rolling six-month window: 48 clients with complete contacts, acquisition sources, services with quarterly price history, provider-availability-aware appointments, realistic statuses, soft-deleted history, lesson notes, payments with at most two unpaid lessons per client, prepayments, categorized monthly expenses, and six weekly recurrence rules. Planned appointments extend 21 days beyond initialization. The seed uses up to four existing non-client staff members; on a fresh database every generated appointment is assigned to the deterministic identity.

```text
Email: dev.superuser@melodytrack.local
Password: MelodyTrack-Development-Only!
TOTP setup key: JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP
```

Add the TOTP setup key to an authenticator as a time-based token with issuer `MelodyTrack`, SHA-1, six digits, and a 30-second period. These credentials are development-only and are never configured as production defaults. Production mode preserves bootstrap-invite behavior and logs only an opaque invite reference unless `Initialization__LogBootstrapSecrets=true` (or the legacy recovery environment variable) is set deliberately.
