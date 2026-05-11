# MelodyTrack Roadmap

## Current State

MelodyTrack is a custom CRM backend running on .NET 10. The active solution is `MelodyTrack.slnx`, which currently includes:

- `MelodyTrack.Backend`: ASP.NET Core API.
- `MelodyTrack.Backend.Tests`: integration test project.
- `MelodyTrack.Migrator`: one-shot v1-to-v2 data migration tool.

Other repository directories exist, but they are not part of the active solution unless `MelodyTrack.slnx` is expanded.

## Backend

The backend is structured around FastEndpoints feature folders under `MelodyTrack.Backend/Api`. Existing domains include auth, clients, services, payments, expenses, users, and schedule.

Core runtime stack:

- .NET 10, pinned through `global.json`.
- FastEndpoints 8.1 for endpoints, validation, auth helpers, Swagger, and integration testing support.
- EF Core 10 with PostgreSQL through `Npgsql.EntityFrameworkCore.PostgreSQL`.
- Quartz with persistent PostgreSQL storage for recurring appointment jobs.
- Serilog and SerilogTracing for console logging and ASP.NET Core request tracing.
- Facet 6.5 for DTO generation and mapping.
- UaDetector 5.1 for parsing session device information from `User-Agent` and request headers.
- JWT bearer auth, invite-code registration, refresh tokens, sessions, password reset, and TOTP/recovery-code 2FA.

Startup currently requires these environment variables:

- `ASPNETCORE_ENVIRONMENT`
- `MELODY_TRACK_APP_DOMAIN`
- `MELODY_TRACK_DATABASE_URL`
- `MELODY_TRACK_JWT_SIGNING_KEY`

On startup the app validates that the seeded `Superuser` role exists and logs a superuser invite link if no superuser has been created.

## Data Model

The current model uses ULIDs for entity IDs and PostgreSQL as the primary database. EF migrations are stored in `MelodyTrack.Backend/Data/Migrations`.

Main entities include:

- Users, roles, sessions, invite codes, recovery codes, and password restoration requests.
- Clients and client contacts.
- Services and service price history.
- Payments and expenses.
- Appointments, recurrence rules, and recurrence types.

Fuzzy search uses PostgreSQL `fuzzystrmatch`, and helper extensions live in `MelodyTrack.Backend/Extensions`.

## Background Jobs

Recurring appointment creation is handled by Quartz:

- Job: `CreateRecurringAppointments`.
- Schedule: weekly trigger configured in `Program.cs`.
- Storage: PostgreSQL persistent Quartz tables.
- SQL bootstrap script: `MelodyTrack.Backend/quartz.sql`.

## Testing

The test suite is integration-oriented:

- xUnit v3.
- FastEndpoints.Testing.
- Shouldly assertions.
- Testcontainers PostgreSQL.
- The test fixture maps `quartz.sql` into the PostgreSQL container and runs EF migrations in `ASPNETCORE_ENVIRONMENT=Test`.

Current verified state:

- `dotnet restore MelodyTrack.slnx -v minimal` passes.
- Project-level builds pass with zero warnings.
- `dotnet test MelodyTrack.Backend.Tests/MelodyTrack.Backend.Tests.csproj --no-build` passes: 59 tests.

## Deployment

The backend Dockerfile targets .NET 10 images:

- `mcr.microsoft.com/dotnet/aspnet:10.0`
- `mcr.microsoft.com/dotnet/sdk:10.0`

## Known Quirks

- Local integration tests require Docker and permissions for the .NET test runner to open local sockets.
- Runtime configuration is environment-variable driven; missing required variables fail startup immediately.
- Development launch settings contain sample local values and should not be treated as production secret management.

## Near-Term Roadmap

1. Harden authentication/session flows.

   - ✅ Add focused tests for refresh-token replay, device-info parsing fallbacks, expired reset flows, and 2FA recovery-code reuse.
   - ✅ Tighten refresh-token handling for expiry, replay detection, and session revocation.
   - ✅ Reject expired password-reset tokens.
   - ✅ Stop returning reset tokens/links to the client and log the full reset URL for developer-mediated recovery.
   - ✅ Prevent anonymous 2FA verification from binding a new secret to an existing user.
   - ✅ Return explicit human-readable problem details for invalid invite links, used/expired invite codes, duplicate registration, and expired reset links.
   - ✅ Expose fresh recovery codes immediately after 2FA recovery/reset flows.
   - ✅ Standardize auth event logs for login, logout, invite acceptance, password resets, and 2FA changes without introducing duplicate audit channels.

2. ✅ Review API authorization boundaries.

   - ✅ Restrict invite creation to admin/superuser callers.
   - ✅ Keep superuser invite creation superuser-only.
   - ✅ Restrict role lookup to admin/superuser callers and hide superuser role from non-superusers.
   - ✅ Confirm `/users` remains admin-only.
   - ✅ Confirm payments, expenses, and schedule endpoints remain authenticated staff-facing operations after review; no extra role gate added.

3. Improve recurring appointment coverage.

   Expand recurrence tests around monthly boundaries, provider-specific recurrence, cancellation/completion behavior, and idempotent Quartz runs.
