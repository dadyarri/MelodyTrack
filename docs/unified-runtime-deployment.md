# Unified production runtime

MelodyTrack production uses one application image:

```text
ghcr.io/<owner>/melody-track:<version>
```

The image entrypoint runs `MelodyTrack.Init --mode production` first. Kestrel starts only after initialization succeeds. The final stage is based on the ASP.NET runtime image and contains neither Node.js nor nginx.

## Compose cutover

The homelab Compose definition is maintained outside this repository. Replace the separate backend and frontend services with one MelodyTrack service while retaining the shared PostgreSQL connection, secrets, restart policy, and Caddy network. Do not add a MelodyTrack-specific PostgreSQL container.

Configure at least:

```text
Database__ConnectionString=<shared PostgreSQL connection>
PublicUrl__BaseUrl=https://melodytrack.example
AuthenticationSecrets__JwtSigningPrivateKey=base64:<P-256-PKCS8-private-key>
AuthenticationSecrets__PasswordPepper=base64:<32-random-bytes>
AuthenticationSecrets__PortalPinPepper=base64:<32-independent-random-bytes>
AuthenticationSecrets__RefreshTokenHashKey=base64:<32-independent-random-bytes>
AuthenticationSecrets__CsrfSigningKey=base64:<32-independent-random-bytes>
PersonalData__CurrentKey=<secret>
PersonalData__CurrentKeyVersion=v1
ReverseProxy__KnownNetworks__0=<Caddy Docker network in CIDR form>
```

The Stage 8 database migration is intentionally breaking. Before applying it, verify the production backup and the server-local recovery command. The migration revokes every existing session, invalidates existing staff passwords and reset requests, and clears legacy portal links, PINs, and saved portal identities. These credentials cannot be restored by rolling the schema back. After Init completes, issue the first superuser reset URL from a controlled server shell as documented in `MelodyTrack.Init/README.md`; administrators can then rotate client portal links and recover other staff accounts through normal workflows.

`Http__PathBase` defaults to `/api`. The frontend is compiled with `VITE_API_BASE_URL=/api`, so browser API traffic stays same-origin. Calendar subscription links are consequently generated under `/api/calendar-subscriptions/...`.

Caddy should route the application host to the single Kestrel container without stripping `/api`. Keep `/health`, `/alive`, and `/otel` private: Compose may use `/health` for readiness, but the public Caddy route must not expose these infrastructure endpoints. Forwarded headers are accepted only from the configured proxy addresses or networks; when no trusted proxy is configured, MelodyTrack safely ignores them and rate limiting uses the direct peer address.

After deployment, verify the root SPA, a nested browser route, a known and unknown `/api` route, cache headers, compression, security headers, and the internal health check before removing the old service definitions and image references.
