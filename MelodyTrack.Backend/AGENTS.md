# Backend Repository Guidelines

## Scope

These instructions apply to the `MelodyTrack.Backend` project. The repository-root `AGENTS.md` defines shared workflow, roadmap, verification, Git, and security rules and takes precedence if there is a conflict.

This file intentionally describes durable backend conventions plus migration boundaries. Do not preserve legacy FastEndpoints/startup behavior merely because it appears in old code.

## Current Backend

The current backend uses .NET 10, ASP.NET Core, EF Core, PostgreSQL, Quartz, Serilog, JWT authentication, TOTP 2FA, and xUnit/Testcontainers integration tests. FastEndpoints, FastEndpoints.Swagger, FluentValidation, startup database initialization, and the existing repository split are **legacy implementation details scheduled for replacement by the active roadmap**. Do not expand their use unless required for an intermediate compatibility step.

Use the SDK selected by `global.json`. PostgreSQL remains the only application database provider.

## Stable Backend Conventions

- Follow `.editorconfig`: file-scoped namespaces, nullable reference types, implicit usings, four-space indentation, braces on new lines, and existing naming conventions.
- Prefer `var` where the type is apparent or unimportant.
- Keep comments sparse and useful.
- Preserve Russian user-facing API/validation messages unless localization is explicitly in scope.
- Use `Ulid` consistently for entities that already use ULIDs.
- Normalize persisted timestamps to UTC.
- Inject `TimeProvider` into clock-sensitive application logic; do not add direct application dependencies on `DateTime.UtcNow` or `DateTimeOffset.UtcNow`.
- Capture one clock value before a multi-step operation that must observe a consistent instant.
- Do not place live-clock initializers on entity properties.
- Use `AsNoTracking()` for read-only EF queries when tracking is unnecessary.
- Keep PostgreSQL-specific behavior where the application depends on it; do not introduce a second runtime database provider.

## API Migration Rules

During the Minimal API migration:

- migrate FastEndpoints feature-by-feature; temporary coexistence is allowed when required for a safe incremental migration;
- do not introduce new FastEndpoints abstractions that will immediately be removed;
- new target endpoints use the roadmap-defined class-level `[ApiEndpoint(ApiMethod, route)]` convention and generated registration;
- target endpoint classes expose exactly one `public static HandleAsync` and accept a `CancellationToken`;
- use ordinary Minimal API parameter injection for services and `AppDbContext`;
- direct `AppDbContext` access is allowed for straightforward endpoint-specific data access;
- use typed Minimal API results by default;
- keep native ASP.NET Core authorization, validation, OpenAPI, DI, serialization, and Problem Details as framework concerns rather than extending the custom generator to reproduce them;
- keep coarse role authorization in centralized DB-backed policies and resource-specific checks close to the operation;
- the real backend route prefix is `/api`; do not add route-version prefixes unless explicitly requested later.

Do not add a generic repository/unit-of-work layer, `MelodyTrack.Application`, or a separate HTTP contracts assembly as part of this refactor.

## Data and Initialization

The target ownership is:

- `MelodyTrack.Core`: EF-free domain primitives/abstractions;
- `MelodyTrack.Data`: EF Core, `AppDbContext`, configuration, migrations, converters, DB initialization implementation;
- `MelodyTrack.Init`: executable orchestration for migrations/backfills/Quartz schema/invariants/environment-specific seed/bootstrap;
- `MelodyTrack.Backend`: HTTP/business services/Quartz runtime.

Until that split exists, preserve current behavior while moving responsibilities deliberately. Do not add new startup migration/backfill/seed logic to Backend when it belongs in `MelodyTrack.Init`.

For schema changes:

- generate migrations with `dotnet ef migrations add`;
- never hand-edit generated designer files;
- review both `Up` and `Down` and inspect destructive changes carefully;
- keep migrations focused;
- do not combine unrelated data rewrites with schema work;
- production schema changes require an approved backup before application.

## Existing Domain/Service Boundaries

Preserve these established behaviors unless the active task explicitly changes them:

- use `ICurrentUserAccessor` rather than reparsing identity/session claims throughout request code;
- preserve idempotent-create semantics through `IRequestReplayService`, `Idempotency-Key`, request fingerprinting, and the existing transaction/concurrency guarantees;
- preserve recurring-task responsibility boundaries under `Services/RecurringTasks`; do not collapse specialized candidate/evaluation/transition/query/render/presentation responsibilities back into one orchestration service;
- preserve recurrence semantics, session revocation semantics, audit behavior, and public URL behavior while relocating implementation.

When the roadmap replaces one of these mechanisms, migrate its behavior and tests before removing the old implementation.

## Authentication and Security Migration

Follow the active roadmap rather than legacy authentication wiring. In particular:

- the target JWT algorithm is ES256 with explicit issuer/audience/signature/lifetime validation;
- password, portal-PIN, refresh-token hashing, CSRF signing, JWT signing, and PII encryption use purpose-separated secrets;
- the auth cutover may revoke all existing sessions and force password/PIN reset; do not retain the legacy shared pepper merely for compatibility;
- keep refresh rotation/replay detection, CSRF protection for cookie-authenticated session operations, TOTP/recovery behavior, and server-side active-session checks;
- portal links are security-sensitive long-lived credentials and must not appear raw in logs/telemetry;
- do not add account lockout state; use the roadmap-defined throttling/cooldown model;
- keep PII field encryption as versioned AES-GCM and move re-encryption/backfill ownership to Init.

## Quartz and Background Work

Quartz remains hosted in `MelodyTrack.Backend`; do not create a separate worker service or clustering/leader-election system unless requirements change explicitly.

Database/schema initialization for Quartz belongs in `MelodyTrack.Init` after that project exists. Jobs should accept/propagate cancellation and use normal application observability conventions.

## Observability

The target application telemetry APIs are `ILogger<T>`, `ActivitySource`, and `Meter`, exported through OpenTelemetry. Serilog may remain the logging provider; do not add new tracing through SerilogTracing.

Do not emit secrets/PII into logs, traces, metrics, exception metadata, or request logging. Preserve the canonical W3C/OpenTelemetry trace ID across response headers and Problem Details once that migration lands.

## Testing

Backend integration tests should remain normal `dotnet test` tests. The target harness is `WebApplicationFactory<Program>` + PostgreSQL Testcontainers + the real `MelodyTrack.Init --mode test`; Aspire AppHost is not a dependency of the normal integration suite.

Add focused tests when changing authentication/session behavior, authorization, recurrence, idempotency, database queries, initialization, public URL generation, or security-sensitive behavior.

Do not run tests/builds automatically after intermediate edits; follow the workspace-root verification policy.

## Generated and Legacy Code

- Do not hand-edit generated EF designer files.
- Once Kiota/OpenAPI generation is integrated into the monorepo build, treat generated client files according to the root rules.
- Remove FastEndpoints/FastEndpoints.Swagger/FluentValidation packages and obsolete startup/migration code only after their replacements are complete and verified.
- Remove obsolete environment variables and configuration validation paths when the strongly typed Options replacement is complete; do not keep dead configuration aliases indefinitely.
