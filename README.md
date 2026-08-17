# MelodyTrack

MelodyTrack is maintained as one repository with separate backend and frontend development projects and one production application runtime.

## Layout

- `MelodyTrack.Backend/` — .NET 10 ASP.NET Core API
- `MelodyTrack.Core/` — EF-free domain abstractions
- `MelodyTrack.Data/` — EF Core context, migrations, persistence, and reusable initialization
- `MelodyTrack.Init/` — database initialization executable
- `MelodyTrack.AppHost/` — Aspire development orchestrator
- `MelodyTrack.ServiceDefaults/` — shared development telemetry, health, resilience, and service discovery defaults
- `MelodyTrack.Backend.Tests/` — xUnit/Testcontainers integration tests
- `MelodyTrack.Web/` — Vite, React, and TypeScript frontend
- `changelog/releases/` — one JSON file per application release
- `scripts/` — repository-wide release and maintenance tooling

## Development

Docker, .NET 10, and Node.js 26 are required for the complete development stack. From a clean clone:

```text
dotnet restore MelodyTrack.slnx
dotnet build MelodyTrack.slnx
dotnet run --project MelodyTrack.AppHost
```

AppHost starts the Aspire Dashboard and a persistent PostgreSQL container, runs Init in development mode, starts Backend on `http://localhost:5000` only after Init succeeds, and then starts Vite on `http://localhost:5173`. Development Init creates or upgrades a representative rolling six-month data set plus three weeks of planned appointments without duplicating it on later starts. Aspire installs the frontend dependencies when needed. Browser `/api/*` requests stay on the Vite origin and are proxied to Backend without rewriting the prefix. Both ports are fixed and startup fails clearly if either is already occupied. Backend and Init send their development logs, traces, and metrics to the Dashboard through OpenTelemetry.

Development SQL parameter logging is disabled by default. Enable it deliberately for one AppHost run with:

```text
Development__EnableSqlParameterLogging=true dotnet run --project MelodyTrack.AppHost
```

The PostgreSQL data volume is named `melodytrack-postgres-data` and survives AppHost restarts. The existing `dotnet test MelodyTrack.slnx` and `npm run verify --prefix MelodyTrack.Web` commands remain the verification entry points. See `roadmap.md` for the active migration contract, `docs/releases.md` for the release workflow, and `docs/frontend-verification-inventory.md` for the custom frontend checks that must be preserved or replaced deliberately.

## Build and publish

The root solution build is the fast cross-stack compatibility build. It builds .NET, bootstraps frontend dependencies only when the package inputs or Node environment changed, and runs the frontend type check:

```text
dotnet build MelodyTrack.slnx
```

Individual project builds remain scoped and do not invoke the frontend. Publishing Backend builds the production Vite bundle and places it under the published `wwwroot`, producing a complete independently runnable application artifact:

```text
dotnet publish MelodyTrack.Backend/MelodyTrack.Backend.csproj -c Release
```

Production uses the single `ghcr.io/<owner>/melody-track` image. Its entrypoint runs `MelodyTrack.Init --mode production` before Kestrel, and Kestrel serves both `/api/*` and the SPA. See `docs/unified-runtime-deployment.md` for the reverse-proxy and Compose cutover contract.
