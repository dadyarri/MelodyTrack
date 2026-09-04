# Production telemetry

## Scope

Only `MelodyTrack.Backend` and `MelodyTrack.Init` export logs, traces, and metrics through OpenTelemetry.

Frontend telemetry, browser OTLP endpoints, session replay, and source-map symbolication are not deployed. The frontend only displays backend-provided trace IDs.

## Topology

```text
MelodyTrack.Init ─────┐
                     ├── OTLP/gRPC ──> Aspire Dashboard
MelodyTrack.Backend ──┘                    │
                                          │ internal Docker network
                                          ▼
                               Caddy HTTPS, LAN access only
```

The Aspire Dashboard runs as a standalone container in the external homelab Compose stack, not as part of the MelodyTrack application deployment.

## Dashboard deployment

Use a pinned version of:

```text
mcr.microsoft.com/dotnet/aspire-dashboard:<version>
```

The container uses these internal ports:

- `18888`: Dashboard UI;
- `18889`: OTLP/gRPC;
- `18890`: OTLP/HTTP.

Do not expose the OTLP ports publicly. Caddy may proxy the UI port, but access must be restricted to the LAN. See the official [standalone Dashboard documentation](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard/standalone).

Configure the Dashboard with secrets supplied by the infrastructure environment:

```text
DASHBOARD__FRONTEND__AUTHMODE=BrowserToken
DASHBOARD__FRONTEND__BROWSERTOKEN=<random-browser-token>

DASHBOARD__OTLP__AUTHMODE=ApiKey
DASHBOARD__OTLP__PRIMARYAPIKEY=<random-key-with-at-least-128-bits-of-entropy>

ASPIRE_DASHBOARD_API_DISABLED=true
```

Do not enable `ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS` in production. The supported authentication settings are listed in the official [Dashboard configuration reference](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard/configuration).

Browser-token authentication supplements the Caddy LAN restriction; it does not replace it.

## MelodyTrack configuration

Provide these variables to the production MelodyTrack container:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:18889
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
OTEL_EXPORTER_OTLP_HEADERS=x-otlp-api-key=<same-OTLP-key>
```

Because Init and Backend run from the same production container, both receive these variables. They retain distinct service names:

```text
melodytrack-init
melodytrack-backend
```

The OTLP key must be stored as an infrastructure secret and must never be committed, logged, exposed to the frontend, or included in diagnostics.

## Network and proxy rules

- Dashboard and MelodyTrack share a private Docker network.
- Only trusted workloads may send telemetry to the internal OTLP endpoint.
- Caddy exposes only the Dashboard UI.
- Dashboard UI access is limited to trusted LAN ranges and protected by HTTPS.
- OTLP ports are not routed through the public Caddy virtual host.
- No `/otel` route exists in MelodyTrack.
- Internal unencrypted OTLP is acceptable only on the trusted Docker network; use TLS if telemetry crosses an untrusted boundary.

## Retention

Initial telemetry retention is in memory. Restarting the Dashboard may discard existing traces, logs, and metrics.

This is acceptable for short-term diagnostics. If durable retention becomes necessary, evaluate a dedicated telemetry backend separately rather than expanding MelodyTrack itself.

## Security and privacy

Production telemetry must not contain:

- passwords, PINs, JWTs, refresh tokens, or CSRF tokens;
- database connection strings or OTLP keys;
- request or response bodies by default;
- SQL parameter values;
- raw portal links;
- unnecessary client or user identifiers.

SQL operation and table labels may be recorded, but production parameter logging and EF sensitive-data logging remain disabled.

## Verification

After deployment:

1. Open the Dashboard through its LAN-only HTTPS address.
2. Authenticate with the browser token.
3. Confirm `melodytrack-init` and `melodytrack-backend` appear as separate services.
4. Make a normal API request and confirm its backend and PostgreSQL spans appear.
5. Trigger a controlled backend error.
6. Copy the trace ID returned by MelodyTrack.
7. Find the same trace in the Dashboard.
8. Confirm the response header, Problem Details payload, logs, and trace use the same ID.
9. Stop the Dashboard and verify Init, Backend, and health checks continue working.
10. Restart the Dashboard and verify newly emitted telemetry appears.

## Key rotation

The Dashboard supports a secondary OTLP key:

```text
DASHBOARD__OTLP__SECONDARYAPIKEY=<new-key>
```

To rotate without interruption:

1. configure the new key as secondary;
2. update MelodyTrack's `OTEL_EXPORTER_OTLP_HEADERS`;
3. verify telemetry arrives using the new key;
4. promote or retain the new key as primary;
5. remove the old key.

## Failure handling

Dashboard or exporter failure must not prevent database initialization, application startup, requests, or scheduled work.

If telemetry causes operational trouble, remove `OTEL_EXPORTER_OTLP_ENDPOINT` from the MelodyTrack environment and redeploy. MelodyTrack continues using its ordinary logs without OTLP export.
