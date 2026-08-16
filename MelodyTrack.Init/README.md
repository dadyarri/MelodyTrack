# MelodyTrack.Init

Run database initialization before starting the backend:

```text
dotnet MelodyTrack.Init.dll --mode production
dotnet MelodyTrack.Init.dll --mode development
dotnet MelodyTrack.Init.dll --mode test
```

Configuration uses standard .NET keys, including `Database__ConnectionString`, `PublicUrl__BaseUrl`, `AuthenticationSecrets__JwtSigningKey`, `PersonalData__CurrentKeyVersion`, and `PersonalData__CurrentKey`. Existing `MELODY_TRACK_*` production variables are mapped during the transition, except the removed separate API base URL.

Development mode creates an idempotent representative data set and the deterministic non-production staff identity below:

```text
Email: dev.superuser@melodytrack.local
Password: MelodyTrack-Development-Only!
```

These credentials are development-only and are never configured as production defaults. Production mode preserves bootstrap-invite behavior and logs only an opaque invite reference unless `Initialization__LogBootstrapSecrets=true` (or the legacy recovery environment variable) is set deliberately.
