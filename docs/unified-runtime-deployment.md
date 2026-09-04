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
WebPush__Enabled=true
WebPush__Subject=mailto:<operational-contact@example.com>
WebPush__PublicKey=<VAPID-public-key>
WebPush__PrivateKey=<VAPID-private-key-secret>
```

Generate the VAPID key pair separately from every authentication and personal-data key. The public key is intentionally returned to authenticated browsers; the private key is a production secret and must stay in the deployment secret store. If Web Push is not configured, leave `WebPush__Enabled=false`; in-app notifications continue to work normally.

## Out-of-band emergency access

God mode runs as a dedicated listener in the main Backend process. The production image binds internal port `8081` automatically. Mount a server-local directory owned by the container application UID at `/var/lib/melodytrack`, generate a dedicated 32-byte signing key, and configure:

```text
GodMode__Port=8081
GodMode__StateDirectory=/var/lib/melodytrack/god-mode
GodMode__PublicBaseUrl=https://god-mode.melodytrack.internal
GodMode__SessionSigningKey=base64:<32-independent-random-bytes>
```

Publish container port `8080` only to the normal application route. Attach `8081` only to the private Caddy network and route the dedicated god-mode hostname to it; never publish `8081` directly on the host's public interface. The server-local command and its short-lived one-time token are the access boundary. Caddy remote-IP matchers are optional defense-in-depth when the surrounding network does not already provide the desired restriction. MelodyTrack rejects god-mode paths on the normal listener and rejects normal application paths on the god-mode listener, so the two surfaces remain separated even if a reverse-proxy route is overly broad.

From a direct server shell, create a five-minute, one-time bootstrap link with:

```text
docker exec <melodytrack-container> melodytrack god-mode
```

The container command delegates to the `MelodyTrack.Init god-mode` subcommand. It only writes the one-time token record and does not run database initialization. The command stores only a SHA-256 token hash in the mounted server-local directory. Opening the printed fragment URL consumes that token and creates a 30-minute `HttpOnly`, `Secure`, `SameSite=Strict` god-mode cookie. The session signature uses the dedicated god-mode key and does not query ordinary users, passwords, 2FA, roles, or MelodyTrack sessions. Do not paste the printed URL into logs, chat, tickets, or monitoring systems.

## Development emergency access

Aspire starts the same Backend process with a dedicated local HTTPS endpoint at `https://localhost:5001`. No Caddy instance or manual God Mode configuration is required. Start the normal development environment:

```shell
dotnet run --project MelodyTrack.AppHost
```

Then generate a one-time link from another terminal:

```shell
dotnet run --project MelodyTrack.Init --launch-profile development -- god-mode
```

Open the printed `https://localhost:5001/god-mode/#token=...` URL. The development launch profiles use a development-only signing key and `/tmp/melodytrack-god-mode`; neither value is used by production.

The Stage 8 database migration is intentionally breaking. Before applying it, verify the production backup and the server-local recovery command. The migration revokes every existing session, invalidates existing staff passwords and reset requests, and clears legacy portal links, PINs, and saved portal identities. These credentials cannot be restored by rolling the schema back. After Init completes, issue the first superuser reset URL from a controlled server shell as documented in `MelodyTrack.Init/README.md`; administrators can then rotate client portal links and recover other staff accounts through normal workflows.

`Http__PathBase` defaults to `/api`. The frontend is compiled with `VITE_API_BASE_URL=/api`, so browser API traffic stays same-origin. Calendar subscription links are consequently generated under `/api/calendar-subscriptions/...`.

Caddy should route the application host to the single Kestrel container without stripping `/api`. Keep `/health` and `/alive` private: Compose may use `/health` for readiness, but the public Caddy route must not expose these infrastructure endpoints. Forwarded headers are accepted only from the configured proxy addresses or networks; when no trusted proxy is configured, MelodyTrack safely ignores them and rate limiting uses the direct peer address.

Configure the separately operated Aspire Dashboard and MelodyTrack's OTLP exporter according to [Production telemetry](production-telemetry.md). The production Dashboard container and its Caddy configuration remain owned by the external homelab infrastructure stack.

## Initialization query behavior

Ordinary production and development initialization uses batched existence and lookup queries. The personal-data backfill is the deliberate exception: it reads each PII table once, decrypts and authenticates values with the application-held key ring, and updates only rows that require encryption or key-version rotation. Those updates remain one command per changed row because set-based SQL cannot safely perform authenticated decryption, detect unknown key versions, or preserve the fail-fast behavior. Init traces report the scanned and updated record counts; plan additional deployment time when rotating a large dataset.

After deployment, verify the root SPA, a nested browser route, a known and unknown `/api` route, cache headers, compression, security headers, and the internal health check before removing the old service definitions and image references.
