# AGENTS.md

## Scope

These instructions apply to the whole repository.

## Project Overview

MelodyTrack is a custom CRM backend written in C# on .NET 10. The active solution is `MelodyTrack.slnx` and currently includes:

- `MelodyTrack.Backend`: ASP.NET Core API using FastEndpoints, EF Core, PostgreSQL, Quartz, Serilog, JWT auth, TOTP 2FA, and Facet DTO mapping.
- `MelodyTrack.Backend.Tests`: xUnit v3 integration tests using FastEndpoints.Testing, Shouldly, and Testcontainers PostgreSQL.
- `MelodyTrack.Migrator`: one-shot hosted-service migrator from the legacy v1 PostgreSQL schema to the v2 backend schema.

Other directories may exist in the repository, but do not assume they are part of the active backend solution unless `MelodyTrack.slnx` is updated.

## Required Tooling

- Use the .NET SDK from `global.json` (`10.0.100`, rolling forward to the latest installed 10.x feature band is allowed).
- PostgreSQL is the production database provider.
- Docker is required for the integration test suite because tests start a PostgreSQL container.

## Common Commands

Run from the repository root:

```bash
dotnet restore MelodyTrack.slnx
dotnet build MelodyTrack.slnx
dotnet test MelodyTrack.slnx
```

For local backend development:

```bash
dotnet run --project MelodyTrack.Backend
```

The `http` launch profile runs on `http://localhost:5230` and expects a frontend origin of `http://localhost:5173`.

## Runtime Configuration

`MelodyTrack.Backend` reads required configuration from environment variables, not from optional config binding. Missing variables throw during startup.

Required backend variables:

- `ASPNETCORE_ENVIRONMENT`
- `MELODY_TRACK_APP_DOMAIN`
- `MELODY_TRACK_DATABASE_URL`
- `MELODY_TRACK_JWT_SIGNING_KEY`

The development launch profile contains local sample values. Do not commit real secrets.

Required migrator variables:

- `MELODYTRACK_V1_DATABASE_URL`
- `MELODYTRACK_V2_DATABASE_URL`

Note the migrator variable names do not include the underscore after `MELODY`.

## Architecture Conventions

- API features live under `MelodyTrack.Backend/Api/<Feature>/` and are split into `Endpoints`, `Requests`, `Responses`, and `Validators`.
- Endpoints use FastEndpoints classes such as `Ep.Req<TRequest>.Res<TResponse>` and return typed ASP.NET Core results.
- Keep request validation in FastEndpoints/FluentValidation validators where possible. Existing validation messages are in Russian; keep user-facing validation errors consistent.
- Data models live in `MelodyTrack.Backend/Data/Models`.
- EF Core configuration currently lives mostly in `AppDbContext` and data annotations on models.
- Migrations live in `MelodyTrack.Backend/Data/Migrations`. Add migrations for schema changes instead of editing generated migration output by hand except for deliberate migration fixes.
- IDs use `Ulid`; new entities generally assign `Ulid.NewUlid()`.
- Date/time values stored in PostgreSQL should be normalized to UTC when accepting request input.
- Use `AsNoTracking()` for read-only queries.
- Shared query helpers live in `MelodyTrack.Backend/Extensions`, including pagination, fuzzy search, and date range filtering.
- Fuzzy search depends on the PostgreSQL `fuzzystrmatch` extension and `[FuzzyPath]` for nested string properties.
- DTO projection/mapping uses Facet in some response types. Prefer existing Facet patterns before adding manual duplicate DTO mapping.
- Background jobs use Quartz with a persistent PostgreSQL store. `quartz.sql` is needed when preparing databases for Quartz tables.

## Authentication And Authorization

- JWT auth is configured with FastEndpoints security and `MELODY_TRACK_JWT_SIGNING_KEY`.
- Auth endpoints under `Api/Auth` include registration, login, refresh, sessions, invite codes, password reset, and 2FA flows.
- Admin-like users require TOTP during login. Regular users can log in without TOTP unless they have a TOTP secret.
- Startup checks that the seeded `Superuser` role exists and creates/logs a superuser invite link if no superuser exists.

## Testing Notes

- Tests are integration-style and boot the real app through `AppFixture<Program>`.
- `MelodyTrackFixture` starts a PostgreSQL Testcontainer, maps `MelodyTrack.Backend/quartz.sql` into the container init directory, sets test environment variables, and lets the app run migrations in `ASPNETCORE_ENVIRONMENT=Test`.
- Tests may fail if Docker is unavailable or if the PostgreSQL image cannot be pulled.
- Prefer focused tests in `MelodyTrack.Backend.Tests` for endpoint behavior, auth/session behavior, recurrence generation, and database-query behavior.
- When testing login endpoints, set a valid `User-Agent` header if device information is part of the flow.

## Style

- Follow `.editorconfig`.
- Use file-scoped namespaces, nullable reference types, implicit usings, four-space indentation, and braces on new lines.
- Prefer `var` where the type is apparent or unimportant, matching current code style.
- Keep public types and members in PascalCase; private instance fields use `_camelCase` when needed.
- Use collection expressions where they fit existing style.
- Keep comments sparse and useful.

## Change Discipline

- Keep changes scoped to the active backend solution unless explicitly asked to touch other projects.
- Do not alter generated EF migration designer files unless the migration itself requires regeneration or a precise manual repair.
- Do not introduce another database provider for app code; SQLite appears only as a referenced package and PostgreSQL is the configured runtime provider.
- Avoid broad refactors in endpoint signatures, auth utilities, model relationships, or Quartz setup without tests.
- Preserve Russian user-facing API messages unless the task explicitly asks to change localization.

## Safe Migration Process

This document defines the expected migration flow for MelodyTrack environments.

Scope:

- EF Core schema migrations for `MelodyTrack.Backend`
- Quartz schema bootstrap through `quartz.sql`
- startup validation and post-deploy smoke checks

This is intentionally separate from the backup/restore verification work. Production changes must still have an approved backup available before any schema change is applied.

### General Rules

1. Apply migrations from the active backend solution only: `MelodyTrack.slnx`.
2. Never hand-edit generated migration designer files.
3. Create new migrations from code changes first, then review the generated SQL shape before applying them anywhere shared.
4. Do not combine unrelated schema changes into one migration.
5. Treat production migrations as a gated deployment step, not as something to discover during app startup.
6. If a migration contains destructive operations such as `DropColumn`, `DropTable`, or data rewrites, require explicit review before applying it outside local development.

### Local Development

Use local startup-driven migrations when working alone on a feature.

1. Make the model changes in `MelodyTrack.Backend`.
2. Generate a migration:

```bash
dotnet ef migrations add <Name> --project MelodyTrack.Backend/MelodyTrack.Backend.csproj
```

3. Review the generated migration for unintended destructive changes.
4. Build the backend:

```bash
dotnet build MelodyTrack.slnx
```

5. Start the API with local environment variables configured.
6. Let startup apply migrations automatically.
7. Confirm startup succeeds without:
    - missing environment variable failures
    - missing seed/reference data failures
    - Quartz bootstrap failures

If startup fails after a migration was generated but before it was committed, fix the model or migration immediately before continuing.

### Creating New Migrations

When adding a migration:

1. Name it after the user-facing or domain change, not a temporary implementation detail.
2. Keep the migration focused on one concern.
3. Check both `Up` and `Down` methods for correctness.
4. Avoid mixing seed-data fixes with unrelated schema changes unless they are required together.

When a change introduces new required startup data, update startup validation in the same branch so bad deployments fail fast.
