# MelodyTrack Roadmap

This roadmap contains active or deliberately deferred work only. Completed implementation history belongs in Git, not in this document.

The roadmap is ordered by dependency. **The refactor program is the mandatory first block. Do not begin later product stages until the refactor exit criteria are satisfied, unless a stage explicitly says it can be developed independently.**

This document is written as an implementation contract for a coding agent. Locked architectural decisions are not invitations for redesign. If implementation exposes a concrete incompatibility, document it and make the smallest change that preserves the intent of this roadmap.

---

## 1. Locked target architecture

### 1.1 Repository and deployment model

- Merge the current backend and frontend repositories into one monorepo while preserving frontend Git history.
- Keep backend and frontend as separate development projects, but ship **one production application image/container**.
- Production Kestrel serves both:
  - the ASP.NET Core API under the real `/api/*` prefix;
  - the compiled Vite/React SPA and static assets.
- Remove the dedicated production frontend nginx container after Kestrel reproduces its required behavior.
- Node.js is a build-time dependency only. It must not be present in the final production runtime image.
- Keep the existing homelab Docker Compose stack as the production orchestrator. Do not replace the production stack with Aspire.
- Continue using Compose `include:` fragments where appropriate.
- Keep the existing shared production PostgreSQL server. Do not add a MelodyTrack-specific production PostgreSQL container.
- Do not add Redis/Valkey as a MelodyTrack dependency.
- Production deployment remains manual because SSH access to the homelab is local-network-only. CI builds and publishes release artifacts/images but does not deploy them.

### 1.2 .NET project boundaries

The target .NET solution contains these responsibilities:

- `MelodyTrack.Core`
  - strictly EF-free;
  - domain/database entities where they are genuinely domain concepts;
  - enums, value objects, constants, pure domain utilities;
  - abstractions required by Core/Data, such as the personal-data protection abstraction;
  - no HTTP-specific endpoint marker types.
- `MelodyTrack.Data`
  - EF Core;
  - `AppDbContext`;
  - entity/model configuration;
  - Npgsql registration;
  - migrations;
  - converters;
  - reusable database initialization/backfill implementation;
  - concrete application-level field-encryption integration where persistence requires it.
- `MelodyTrack.Backend`
  - Minimal API HTTP surface;
  - authentication/authorization integration;
  - business services;
  - Quartz scheduler/jobs;
  - SPA/static hosting.
- `MelodyTrack.Init`
  - thin executable over reusable initialization code;
  - owns all database initialization and environment-specific seed/bootstrap operations;
  - is observable as a separate service/process.
- `MelodyTrack.Api.Generators`
  - analyzer/source-generator project referenced by Backend only as an analyzer;
  - no runtime dependency from Backend to the generator assembly;
  - compile-time endpoint discovery/registration only.
- Aspire AppHost and ServiceDefaults projects as required by the current Aspire templates/tooling.

Do **not** introduce a `MelodyTrack.Application`, generic repository, generic unit-of-work, or separate HTTP contracts assembly during this migration. Endpoint handlers may inject and use `AppDbContext` directly for straightforward endpoint-specific data access.

### 1.3 Development orchestration

Development uses Aspire AppHost to orchestrate:

- `MelodyTrack.Init`;
- `MelodyTrack.Backend`;
- Vite development server;
- a dedicated PostgreSQL container;
- a persistent named PostgreSQL volume;
- Aspire Dashboard.

Requirements:

- the development database survives ordinary AppHost restarts;
- Init runs before Backend and Backend starts only after successful Init completion;
- a fresh Development database is migrated and seeded automatically;
- Development seeding is **versioned and idempotent**, not a one-time boolean;
- seed upgrades can evolve an existing persistent development database without deleting the volume;
- Development gets a deterministic non-production superuser/provider and representative demo data;
- Development-only credentials/secrets must never be valid production defaults.

Aspire AppHost is development-only. It is not a production daemon/orchestrator.

### 1.4 API architecture

- Replace FastEndpoints with native ASP.NET Core Minimal APIs.
- Replace FastEndpoints.Swagger/NSwag document generation with native ASP.NET Core OpenAPI.
- Replace FluentValidation with native .NET validation mechanisms suitable for Minimal APIs.
- Keep vertical-slice endpoint organization and one endpoint class per operation.
- `/api` is a globally secured route group. Authentication is required by default.
- Anonymous operations explicitly use native ASP.NET Core `[AllowAnonymous]` metadata on the handler.
- Coarse role authorization is centralized in DB-backed ASP.NET Core policies rather than repeated manual role checks in endpoint bodies.
- Mutable role state is not duplicated into JWT claims.
- Resource-specific authorization remains close to the operation/resource where needed.
- Use central Problem Details for external errors.
- Use typed Minimal API results by default so OpenAPI can infer status codes and schemas.
- The API is not route-versioned as part of this migration. Do not introduce `/v1`, `/v2`, or another version prefix merely because the old Swagger document had a version-like name.

### 1.5 Endpoint registration source generator

Use one custom class-level marker:

```csharp
[ApiEndpoint(ApiMethod.Post, "/clients")]
public sealed class CreateClientEndpoint
{
    public static Task<Results<Created<ClientDto>, ValidationProblem>> HandleAsync(
        CreateClientRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        // ...
    }
}
```

Locked conventions:

- `ApiEndpointAttribute` and `ApiMethod` live in Backend HTTP infrastructure, not Core.
- `ApiMethod` is a custom enum, initially covering the methods MelodyTrack actually needs (`Get`, `Post`, `Put`, `Patch`, `Delete`). Add others only when required.
- The attribute contains the complete route below the global `/api` prefix.
- Do not infer route prefixes from namespaces, folders, feature names, or class names.
- The generator recognizes exactly the MelodyTrack endpoint marker; it does not invent its own authorization, validation, OpenAPI, serialization, or DI systems.
- Every endpoint class has exactly one `public static HandleAsync` handler.
- Every handler has a `CancellationToken` parameter. Missing token is a build diagnostic/error.
- Handler DI uses ordinary Minimal API parameter injection.
- `HandleAsync` may return `Task`/`ValueTask` directly; the C# `async` modifier is not mandatory.
- Endpoint operation ID is inferred from the class name by removing the `Endpoint` suffix, e.g. `CreateClientEndpoint -> CreateClient`.
- Operation IDs must be unique and stable.
- Duplicate HTTP method + route and duplicate operation IDs are compile-time diagnostics.
- Native handler attributes such as `[AllowAnonymous]`, `[Authorize]`, `[EndpointSummary]`, and `[EndpointDescription]` remain framework metadata. The custom generator must not reimplement them.
- Do not add a dedicated Roslyn generator test project initially. Keep the generator small and validate it through compilation/integration tests. Add focused generator tests later only if generator logic grows materially.

### 1.6 OpenAPI and generated frontend client

- Native ASP.NET Core OpenAPI is the sole API contract source.
- Generate OpenAPI 3.1.
- Runtime OpenAPI and Scalar UI are Development-only.
- Build-time OpenAPI generation must be side-effect free: generating a document must not migrate/seed a database, initialize Quartz state, or require unrelated production secrets.
- Use build-time OpenAPI generation to feed Kiota.
- Generated OpenAPI JSON is an untracked/intermediate build artifact. Do not commit it.
- Generate the TypeScript client/models with Kiota.
- Commit Kiota-generated source to Git.
- Generated Kiota models are the source of truth for API request/response/entity DTOs in the frontend.
- Do not keep handwritten TypeScript mirrors of API DTOs. Handwritten TS types remain only for frontend-owned concerns such as form state, component props, UI state, view models, and query convenience types.
- Keep handwritten application-facing API modules such as `clientsApi.create/list/...` over the generated Kiota layer. React/TanStack Query code should not depend directly on low-level generated request builders everywhere.
- Preserve stable operation IDs because generated client shape depends on them.

### 1.7 Frontend architecture

All frontend work continues to follow [Feature-Sliced Design v2.1](https://feature-sliced.design/docs/get-started/overview).

- Use `app`, `pages`, `widgets`, `features`, `entities`, and `shared`, highest to lowest.
- Imports may point only to lower layers; slices on the same layer must not import one another.
- Use intentional public APIs for cross-slice imports.
- Keep domain ownership, query keys, mutations, invalidation rules, and application-facing API wrappers in their owning slice.
- Put code in the lowest layer that accurately owns it. Do not add speculative abstractions, empty layers, or one-file slices for appearance alone.
- Replace Axios with Kiota's standard Fetch-based transport.
- TanStack Query remains the server-state manager.
- Valibot may remain for frontend-owned form/storage validation where useful; it does not duplicate generated API DTO definitions.
- Dexie remains available for browser persistence and later offline work. Persisted data must remain versioned and validated at the storage boundary.
- URL-shareable state belongs in the URL rather than durable forms.
- Keep route-only heavy dependencies behind route/feature boundaries.
- Preserve bundle/asset budgets.
- Keep Steiger, formatting, linting, strict TypeScript, browser tests, builds, security checks, and bundle checks in the mandatory verification pipeline.

### 1.8 Production static hosting behavior

Kestrel must preserve the useful semantics of the old nginx frontend deployment:

- fingerprinted `/assets/*` receive long-lived immutable caching;
- `index.html` is not long-cacheable;
- service-worker files, when present, are not long-cacheable;
- response compression remains enabled;
- existing security headers/CSP behavior is preserved or improved intentionally;
- unknown client-side SPA routes fall back to `index.html`;
- unknown `/api/*` routes never fall back to the SPA;
- `/health`, `/alive`, and other backend infrastructure routes must not be swallowed by SPA fallback;
- production CORS becomes unnecessary after same-origin hosting and should be removed once verified;
- Vite development proxy keeps browser traffic same-origin and must stop stripping the `/api` prefix.

### 1.9 Canonical public URL

Replace separate frontend/API public base URLs with one explicit configured canonical application base URL, for example:

```text
https://melodytrack.example
```

Use it to construct invite, password-reset, portal, calendar-subscription, and other user-facing absolute URLs. Do not derive canonical outbound links from untrusted incoming `Host` headers.

Configure forwarded headers for reverse-proxy operation, but accept them only from explicitly trusted Caddy/reverse-proxy networks/addresses so client-IP rate limiting and security telemetry cannot be trivially spoofed.

### 1.10 Authentication and session model

Target staff authentication:

- short-lived access JWT, approximately the existing 10-minute lifetime;
- ES256 signing using an asymmetric P-256 key;
- explicitly validate algorithm, issuer, audience, signature, and lifetime;
- keep JWT focused on identity/session data rather than mutable role authorization;
- preserve server-side active-session validation using the session ID;
- in-memory browser access token;
- rotating opaque refresh token in an `HttpOnly`, `Secure` production cookie;
- `SameSite=Strict` unless a concrete supported flow requires otherwise;
- DB stores only a keyed hash/reference of refresh credentials;
- refresh-token replay detection remains able to revoke compromised session state;
- session lifetime is sliding: staff refresh activity extends the staff session window, portal refresh activity extends the portal session window;
- preserve approximately 7-day staff inactivity and 30-day portal inactivity unless product requirements later change;
- do not add an absolute maximum session lifetime in this refactor;
- one renewable MelodyTrack identity per browser profile remains the normal model. Staff and portal logins may replace one another's browser refresh cookie. Do not add parallel staff/portal cookie stacks solely for developer convenience.

Cryptographic secrets are purpose-separated:

- `JwtSigningPrivateKey` (secret; public validation material may be derived from it in-process unless a separate public key is operationally useful);
- `PasswordPepper`;
- `PortalPinPepper`;
- `RefreshTokenHashKey`;
- `CsrfSigningKey`;
- PII encryption keys remain a separate key family;
- later Web Push VAPID keys are also separate.

Do not derive all of these from one master secret.

No general-purpose live key-ring rotation system is required initially:

- rotating JWT/refresh/CSRF keys may intentionally invalidate active sessions;
- `PasswordPepper` is a long-lived critical secret and future rotation is an explicit migration event;
- password hashes must carry enough format/version information to support future rehash migrations;
- do not build multi-key compatibility machinery without an actual rotation requirement.

### 1.11 Password and portal PIN hashing

- Migrate from the current Argon2i/low-memory configuration to Argon2id with parameters meeting current project security guidance at implementation time.
- Use unique salts generated by the password hashing implementation.
- Use the dedicated `PasswordPepper` for staff/user passwords.
- Use the dedicated `PortalPinPepper` for portal PINs.
- Do not reuse JWT signing material for password/PIN hashing.
- Because the current production user population is tiny, do **not** carry legacy password/PIN verification into the normal application.
- The auth migration may invalidate existing password/PIN hashes and force credential reset.
- Existing sessions are intentionally revoked during the breaking auth cutover.
- Portal PINs may be cleared; on the next use of a still-valid permanent portal link, the client sets a new PIN.
- Provide a deterministic server-local way to recover the first staff/superuser account during the cutover without requiring another already-authenticated administrator. This may be a narrow migration/recovery command; do not retain the old shared pepper in normal runtime authentication solely for compatibility.

### 1.12 CSRF model

Keep CSRF protection for operations that consume an ambient refresh/session cookie:

- refresh;
- current-session logout or other cookie-authenticated session operations.

Use the existing signed/session-bound double-submit concept, but with its own `CsrfSigningKey`.

Do not attach CSRF headers to every bearer-authenticated API mutation merely as generic ceremony. Bearer-authenticated operations are not protected by the browser automatically attaching a bearer credential.

Preserve CSRF integration tests around cookie-authenticated flows.

### 1.13 Authorization model

Use DB-backed ASP.NET Core authorization policies for coarse permissions, e.g.:

- authenticated/active session;
- staff;
- administrator;
- superuser;
- client portal.

The exact policy names may follow project naming conventions.

Requirements:

- role changes take effect from current database state without waiting for a new JWT;
- endpoint bodies no longer repeat coarse `currentUser.Role` checks where a policy can express the rule;
- resource/ownership checks remain explicit where required;
- portal identities cannot reach staff operations because an endpoint forgot a manual role check;
- session revocation and active-session validation remain separate from role authorization.

### 1.14 Client portal authentication

Preserve the existing portal model rather than turning clients into ordinary email/password accounts:

- a permanent high-entropy reusable portal URL token;
- a client-chosen four-digit PIN;
- successful portal login creates a normal server-side MelodyTrack session with short-lived access token + rotating refresh cookie;
- the permanent portal URL remains reusable/bookmarkable and is a credential;
- portal URL/token material must not appear in logs, traces, analytics, or generic request logging;
- first-use possession of the portal link remains the initial identity proof; do not add SMS/email OTP solely for this migration;
- portal-link rotation/revocation, PIN reset, and explicit administrative session revocation invalidate affected sessions;
- ordinary portal login **must not revoke every other portal device session**;
- allow multiple concurrent portal device sessions;
- keep the current single browser-cookie identity behavior described above.

Because the PIN is only four digits, add persistent per-link online attack resistance:

- keep general ASP.NET rate limiting;
- add a DB-backed escalating temporary cooldown using the existing failed-attempt state or its cleaned-up replacement;
- reset failed-attempt/cooldown state after successful authentication;
- cap cooldown at a reasonable maximum;
- do not use permanent account lockout/admin intervention for ordinary failed guesses;
- partition/add rate limits so a distributed attacker cannot bypass all protection by changing source IP.

### 1.15 PII encryption

Preserve the dedicated versioned application-level AES-256-GCM field encryption subsystem rather than replacing it with ASP.NET Core Data Protection.

Requirements:

- use fresh nonces as required by AES-GCM;
- ciphertext carries its key version;
- configuration supplies actual high-entropy 256-bit keys, not arbitrary strings hashed once to obtain key material;
- old key versions remain readable while migration is in progress;
- `MelodyTrack.Init` owns re-encryption/backfill to the current key version;
- initialization fails if stored encrypted data references an unavailable key version;
- old key versions may be retired only after verification confirms all persisted ciphertext has been migrated;
- PII keys remain completely independent from authentication secrets.

### 1.16 Observability

Use OpenTelemetry as the canonical tracing/metrics/export model.

Application APIs:

- logs: `ILogger<T>`; prefer `LoggerMessage` source generation for stable/high-frequency logging;
- traces: `ActivitySource`;
- metrics: `Meter`.

Keep Serilog as the logging provider where it remains useful. Remove SerilogTracing as the tracing layer. Ensure there is exactly one intended OTLP log-export path so events are not duplicated.

Severity semantics:

- `Trace`: extremely verbose diagnostics;
- `Debug`: internal workflow/success diagnostics;
- `Information`: meaningful lifecycle/state events;
- `Warning`: recoverable anomaly or rejected/abnormal operation;
- `Error`: unexpected operation failure;
- `Critical`: service cannot function.

Audit events remain separate from operational logs.

Do not emit secrets or unnecessary PII into telemetry:

- no JWTs;
- no refresh tokens;
- no CSRF tokens;
- no passwords/PINs;
- no connection strings;
- no request/response bodies by default;
- no raw permanent portal link tokens;
- avoid high-cardinality user/client IDs in metrics.

Backend custom metrics/spans should add business value rather than duplicate framework/runtime instrumentation. Good candidates include auth failures/refreshes, idempotency replay/conflicts, Quartz executions/failures/durations, and important workflow failures.

### 1.17 Trace ID contract

The canonical support trace ID is the W3C/OpenTelemetry trace ID:

```csharp
Activity.Current?.TraceId
```

Requirements:

- keep the external response header name `X-Trace-Id`;
- change its value from `HttpContext.TraceIdentifier` to the active W3C trace ID, falling back only if no Activity exists;
- central Problem Details includes the same `traceId`;
- logs emitted inside the activity correlate naturally through OTel rather than manually duplicating trace IDs everywhere;
- incoming W3C propagation should keep API, database, and outbound spans under one searchable trace;
- integration tests verify the equality of propagated `traceparent`, Problem Details `traceId`, and `X-Trace-Id`.

### 1.18 Frontend error model

Frontend errors normalize into a reusable `AppError` shape carrying appropriate fields such as:

- message/title;
- HTTP status;
- application/error code;
- trace ID;
- validation/field errors;
- error kind.

Presentation rules:

- meaningful failures are persistent in the natural UI context where possible;
- global persistent fallback exists when there is no natural owner;
- short-lived toasts may communicate success/routine transient feedback but are not the only location for important failures;
- important server errors display a trace ID with a dedicated `Copy trace ID` action;
- use a generic text-copy or trace-copy component, not the existing URL-copy modal;
- frontend-only errors expose a frontend trace ID only if one naturally exists; do not invent a fake one after the fact.

Frontend telemetry is intentionally out of scope. Do not add browser OpenTelemetry instrumentation, an OTLP relay, session replay, console forwarding, or source-map publication/symbolication without a concrete later need. The frontend only consumes and presents backend-provided trace IDs for support correlation.

### 1.19 Database telemetry

Do not collect PostgreSQL server logs as part of this migration.

Use EF Core/Npgsql telemetry instead:

- Production: Npgsql OpenTelemetry traces + metrics;
- Production: SQL command text enabled where safe/useful, parameter values disabled;
- Production: Npgsql logs Warning+ by default, with Information temporarily enabled for diagnosis when needed;
- Development: allow explicitly configured parameter/sensitive query visibility;
- do not enable EF sensitive-data logging or Npgsql parameter logging in production;
- do not deploy an OTel Collector solely to ingest PostgreSQL logs.

### 1.20 Production telemetry operations

Production Dashboard deployment, access control, retention, and operator procedures are defined in `docs/production-telemetry.md`; the actual Compose/Caddy deployment remains owned by the external homelab infrastructure stack. This repository only provides standard OTLP exporter configuration for Backend and Init and does not own browser ingestion. Telemetry export failure must never prevent initialization or application startup.

### 1.21 Quartz

Keep Quartz hosted inside `MelodyTrack.Backend`.

- do not add a separate worker container/process;
- do not add clustering, leader election, or multi-replica scheduling support;
- `MelodyTrack.Init` owns Quartz schema initialization;
- jobs propagate cancellation;
- jobs have useful root activities/logging/metrics;
- graceful Backend shutdown waits for running jobs within a bounded timeout;
- Backend assumes initialization has already completed successfully.

### 1.23 Build contract

A root solution build is the cross-stack compatibility build.

```text
dotnet build   # from repository root / solution
```

must:

1. build the .NET solution;
2. generate build-time native OpenAPI without runtime side effects;
3. regenerate committed Kiota TypeScript sources in-place;
4. bootstrap frontend dependencies with `npm ci` only when dependencies are missing/outdated relative to the lockfile;
5. run a fast frontend TypeScript/API compatibility check such as `tsc --noEmit`;
6. fail if generation or type compatibility fails.

Use solution-level MSBuild customization (`Directory.Solution.targets` or a small imported targets file) for this orchestration so individual project builds remain scoped.

Examples:

```text
dotnet build MelodyTrack.Backend.csproj
```

must not recursively invoke the entire frontend pipeline.

Use a deterministic dependency-bootstrap stamp/hash tied to `package-lock.json` (and any other necessary environment identity) rather than blindly running `npm ci` on every build.

CI runs the same generation path and then fails if generated Kiota source changed without being committed:

```text
dotnet build
git diff --exit-code -- <generated-client-path>
```

Full frontend `npm run verify` remains a separate repository/CI quality gate and is not replaced by the fast root-build typecheck.

### 1.24 Publish and image contract

`dotnet publish` for the production Backend application must:

- build the production Vite frontend;
- place the built SPA/static assets into the publish output expected by Kestrel;
- produce a complete runnable application artifact outside Docker.

The Docker build then packages that publish artifact. The final image contains the ASP.NET runtime/application only, not Node or frontend build tooling.

Image integration tests must verify the artifact itself, not an accidental behavior of a separate nginx container.

### 1.25 Testing model

The main backend/integration test suite remains directly runnable with `dotnet test`.

Use:

- xUnit/current test framework;
- PostgreSQL Testcontainers;
- the real `MelodyTrack.Init --mode test` process before Backend startup;
- `WebApplicationFactory<Program>`/standard ASP.NET Core test hosting after FastEndpoints testing infrastructure is removed.

Do not make the main integration suite depend on Aspire AppHost.

Aspire-level distributed tests may be added later only for a concrete cross-resource scenario that cannot be covered more simply.

### 1.26 Release and branch model

Keep `develop`, but it remains intentionally **local-only**.

Branch semantics:

- `master`: released/production history;
- local `develop`: next normal release/integration branch;
- `feature/*`: branch from local `develop`, merge back to `develop`;
- `release/<version>`: cut from `develop`, stabilization only, PR to `master`;
- `hotfix/<version>`: cut from current up-to-date `master`, urgent production fix only, PR to `master`.

After release/hotfix merge:

- merge released `master` forward into local `develop`;
- after a hotfix, also merge it into an active release branch when necessary;
- do not assume `develop` can always be fast-forwarded after a hotfix.

Version semantics:

- regular: `YYYY.MM.N`;
- hotfix: `YYYY.MM.N.H`, derived from the current production regular release;
- hotfix inherits the parent release codename.

Changelog model:

- one root application changelog area, e.g. `changelog/releases/`;
- one file per release/hotfix, e.g. `2026.08.2.json`;
- exactly one active regular release draft on local `develop`;
- active draft has version/codename and `date: null`;
- feature/fix work adds human-written entries to the active draft;
- keep categories such as `new`, `improved`, `fixed`, `security` unless a concrete migration requires adjustment;
- do not replace human-written release notes with conventional-commit generation.

Release tooling:

- retain `ReleaseTool`, but simplify it for one repository;
- remove sibling frontend repository assumptions and `--frontend` style coordination;
- remove obsolete frontend `ReleaseWorkflow.cs` after monorepo migration;
- `start-next-release` derives the next calendar version automatically, with an optional validated explicit override;
- `start-hotfix` derives the next hotfix suffix automatically from production;
- cutting a release automatically creates the next regular draft on local `develop`;
- normal publish flow is CI after a valid release/hotfix PR reaches `master`;
- a manual/recovery publish command may remain idempotent but is not the normal path.

Release CI after merge to `master`:

- validate release metadata;
- run required verification;
- build/push the unified image with version/SHA/latest policy;
- create Git tag;
- create GitHub Release from the changelog;
- do **not** deploy to the homelab automatically.

One version source must feed:

- Docker image version/tags;
- .NET assembly informational version;
- frontend displayed version;
- OTel `service.version`;
- Git tag;
- GitHub Release.

---

## 2. Agent execution rules

These rules apply to every refactor stage.

1. **Preserve behavior unless the roadmap explicitly changes it.** A migration is not permission to redesign unrelated product behavior.
2. **Keep changes bisectable.** Prefer stages/PRs that leave the repository buildable and testable.
3. **Do not leave two permanent implementations.** Temporary coexistence is allowed during migration, but remove the replaced implementation once the replacement is verified.
4. **Do not add speculative abstractions.** Implement only the seams needed by the target architecture.
5. **Do not add compatibility for old releases unless this roadmap explicitly requires it.** Database rollback may require restore/manual correction; old binaries do not need to run against the migrated DB.
6. **Keep OpenAPI generation side-effect free.** Do not reintroduce DB initialization into Backend startup merely to make build-time generation convenient.
7. **Propagate cancellation.** Endpoint `CancellationToken` values should reach EF, outbound HTTP, and other cancellable operations.
8. **Avoid telemetry leakage.** Security-sensitive values never become convenient debugging fields.
9. **Use existing domain semantics.** Recurrence, idempotency, time handling, audit behavior, 2FA/recovery, and other already-correct behavior should be ported, not reinvented.
10. **Update tests with each migration.** Do not postpone all test migration to the end.
11. **Update the roadmap only for meaningful scope/status changes.** Completed implementation details belong in Git history/PRs.

---

# Refactor Program

The refactor program must complete before normal product-stage work resumes.

## Stage 1: Monorepo Merge and Verification Baseline ✅

### Goal

Create a single repository without changing production architecture yet. Establish a trustworthy baseline so later migrations can be separated from repository-merging mistakes.

### Work

- Import the frontend repository into the backend repository while preserving frontend Git history.
- Choose a stable monorepo layout that keeps project boundaries obvious. Avoid an unrelated mass folder rename unless needed for the merge.
- Update relative paths in scripts, CI, documentation, package tooling, browser tests, and build-budget checks.
- Preserve the existing backend and frontend verification commands initially.
- Establish one root solution/build entry point.
- Establish CI jobs with clear logical responsibilities, initially at least:
  - backend verify;
  - frontend verify;
  - contract/build verify placeholder;
  - image/package placeholder.
- Do not add path filtering initially; correctness is more important than shaving CI time during migration.
- Move the current changelog to the root/application-level one-file-per-release structure.
- Begin simplification of release scripts enough that they operate in one repository, but defer deeper release automation cleanup to the final refactor stages.
- Inventory custom frontend scripts:
  - build budget;
  - CSS compatibility;
  - public assets;
  - security baseline;
  - URL-copy boundary;
  - WebKit test workaround;
  - any additional repository-specific checks.
- Mark which scripts are environment-specific and which remain generally useful.

### Important preservation rules

- Do not change API behavior in this stage.
- Do not replace Axios/FastEndpoints/nginx yet.
- Keep the old deployment working until the unified runtime stage is ready.
- Do not lose the frontend Git history.

### Acceptance criteria

- One repository contains both applications and histories.
- Existing backend tests pass from the monorepo.
- Existing frontend `npm run verify` passes from the monorepo.
- Existing production images can still be built before the runtime cutover.
- Release/changelog scripts no longer require a sibling repository merely to inspect metadata.

---

## Stage 2: Shared .NET Projects, Configuration, and Init Boundary ✅

### Goal

Remove database/environment initialization side effects from Backend startup and establish the project dependency graph needed by native build-time OpenAPI and Aspire.

### Work

- Add `MelodyTrack.Core`.
- Add `MelodyTrack.Data`.
- Add `MelodyTrack.Init`.
- Move EF/database infrastructure into Data without moving HTTP/business orchestration there.
- Keep Core EF-free.
- Move existing migration/backfill/bootstrap logic out of Backend startup and into reusable Data initialization components invoked by Init.
- Move/refactor the current local seeding script into the Init system.
- Define Init modes:
  - `production`;
  - `development`;
  - `test`.
- Common Init responsibilities:
  - EF migrations;
  - required data migrations/backfills;
  - Quartz DB/schema initialization;
  - mandatory database invariants;
  - PII encryption-key availability/integrity checks.
- Production-only behavior:
  - preserve existing bootstrap-invite semantics;
  - if no superuser exists, create/reuse the bootstrap invite;
  - safe/reference logging by default;
  - full invite/reset URL only through the existing/explicit recovery flag or equivalent deliberate operator action.
- Development-only behavior:
  - deterministic development superuser/provider;
  - representative seed data;
  - versioned idempotent seed upgrades.
- Test-only behavior:
  - deterministic baseline required by integration tests.
- Replace the custom global `StartupConfigurationValidator.LoadAndValidate(...)` pattern with standard `IConfiguration`, strongly typed Options, and `ValidateOnStart()`.
- Backend and Init each bind only the options they need.
- Keep environment variables/Compose secrets as valid production configuration sources.
- Replace separate frontend/API base URL settings with one canonical `PublicUrlOptions.BaseUrl` (or equivalent).
- Introduce proper production secret option types without implementing the breaking crypto migration yet.

### Dependency direction

Target conceptual dependency direction:

```text
MelodyTrack.Core
      ↑
MelodyTrack.Data
   ↑        ↑
Backend    Init
```

Backend may reference Core directly where appropriate, but Core must not depend on Data/Backend.

### Production process contract

The unified production container later executes:

```text
dotnet MelodyTrack.Init.dll --mode production
    ↓ only on exit code 0
dotnet MelodyTrack.Backend.dll
```

If Init fails:

- write a Critical/fatal diagnostic to console/logging;
- do not start Kestrel;
- let the container fail/restart according to Compose policy.

### Acceptance criteria

- Backend startup no longer performs EF migrations, seed data, Quartz schema setup, or production bootstrap side effects.
- `MelodyTrack.Init --mode test` can initialize an empty PostgreSQL database.
- `MelodyTrack.Init --mode development` can be run repeatedly without duplicating seed data.
- changing the Development seed version upgrades an existing persistent DB without deleting its volume.
- build-time startup of Backend does not require a database or mutate one.
- Options validation fails clearly for missing/invalid required production settings.

---

## Stage 3: Aspire Development Environment and Service Defaults ✅

### Goal

Make the new initialization boundary and development dependencies easy to run consistently.

### Work

- Add Aspire AppHost.
- Add Aspire ServiceDefaults.
- Orchestrate Development PostgreSQL with a persistent named volume.
- Orchestrate Init before Backend.
- Orchestrate the Vite dev server alongside Backend.
- Configure the Vite proxy so browser API calls use `/api/*` without stripping the prefix.
- Configure service discovery/environment injection without making production depend on Aspire AppHost.
- Add development OpenTelemetry defaults through ServiceDefaults.
- Ensure development SQL parameter visibility can be explicitly enabled while production remains safe.
- Document the normal local workflow from a clean clone:
  - restore/build;
  - start AppHost;
  - Init creates/migrates/seeds DB;
  - Backend and Vite become available;
  - Dashboard receives telemetry.

### Acceptance criteria

- A new developer can run the complete development stack without manually creating a DB/schema/user.
- PostgreSQL data survives stopping/restarting AppHost.
- Init failure prevents Backend from starting.
- Vite/API calls are same-origin from the browser perspective.
- AppHost is not referenced by the production container/Compose startup path.

---

## Stage 4: Unified Build, Publish, and Kestrel SPA Hosting ✅

### Goal

Produce one complete application artifact and one production runtime before changing the API framework/client generation.

### Work

- Add root solution-level MSBuild orchestration for cross-stack builds.
- Implement frontend dependency bootstrap based on lockfile/stamp state rather than unconditional `npm ci`.
- Make `dotnet publish` invoke the production Vite build.
- Copy Vite `dist` output into the Backend publish/static root.
- Configure Kestrel static/SPA hosting.
- Reproduce nginx behavior:
  - immutable caching for fingerprinted assets;
  - no long cache for HTML/service-worker entrypoints;
  - compression;
  - CSP/security headers;
  - correct MIME types/public assets;
  - SPA fallback exclusions.
- Keep `/api`, `/health`, and `/alive` outside SPA fallback.
- Preserve `/api/calendar-subscriptions/...` public behavior.
- Remove production CORS after same-origin verification.
- Build one multi-stage Docker image whose final stage contains only the published .NET application/runtime.
- Change production Compose to one MelodyTrack application container.
- Remove the old frontend nginx container only after integration tests prove equivalent behavior.
- Keep Caddy/reverse-proxy routing external to the app container as today.

### Replace nginx-specific verification

The old security-baseline script that parses nginx configuration should become an actual HTTP integration test against the unified published/image runtime. Preserve useful checks rather than preserving nginx-specific implementation assumptions.

### Required image/integration scenarios

- `GET /` serves the SPA.
- a nested client-side route serves SPA fallback.
- a known `/api` endpoint reaches Backend.
- a nonexistent `/api/...` endpoint returns an API 404/ProblemDetails as appropriate and never SPA HTML.
- fingerprinted assets have intended cache headers.
- HTML/service-worker entrypoints have intended cache headers.
- compression works for eligible responses.
- CSP/security headers are present.
- health endpoint remains suitable for Compose health checks.

### Acceptance criteria

- `dotnet publish` output is independently runnable as the complete app.
- one production image/container serves API + SPA.
- Node/nginx are absent from the final runtime image.
- current reverse proxy can route the application without exposing health endpoints publicly.
- old frontend runtime container can be deleted.

---

## Stage 5: Minimal API Foundation and Endpoint Source Generator ✅

### Goal

Introduce native Minimal API infrastructure and migrate endpoints incrementally without turning `Program.cs` into a large routing file or rebuilding FastEndpoints under another name.

### Work

- Add Backend `ApiEndpointAttribute` and `ApiMethod`.
- Add analyzer-only `MelodyTrack.Api.Generators` project reference.
- Implement compile-time endpoint discovery and mapping generation.
- Generate the appropriate `MapGet`/`MapPost`/etc call and `.WithName(operationId)`.
- Add compile-time diagnostics for at least:
  - endpoint class missing required `Endpoint` suffix;
  - missing/multiple `HandleAsync` methods;
  - non-public/non-static handler;
  - missing `CancellationToken`;
  - duplicate operation ID;
  - duplicate method+route;
  - invalid/empty route;
  - unsupported `ApiMethod`.
- Keep generator responsibility limited to discovery/registration/naming/shape diagnostics.
- Configure `/api` route group with default authorization.
- Keep FastEndpoints and Minimal API endpoints coexisting temporarily during migration.
- Establish native authorization metadata use on handlers.
- Establish typed result conventions.
- Migrate feature-by-feature, keeping behavior/tests stable.
- Prefer direct `AppDbContext` use for straightforward endpoint-local queries rather than inventing repository wrappers.
- Preserve existing shared services where logic is reused or substantial.

### Migration order guidance

Start with low-risk/read-only or simple endpoints to validate:

- generated registration;
- auth metadata;
- typed result/OpenAPI metadata;
- validation behavior;
- integration testing.

Then migrate auth/session and complex business endpoints after the native infrastructure is proven.

### Acceptance criteria

- endpoint classes require no manual feature-level registration list;
- operation IDs are stable and unique;
- handlers receive DI/services/cancellation through native Minimal API binding;
- security metadata works without custom generator support;
- all migrated endpoints have equivalent external behavior except intentional roadmap changes;
- once the final endpoint is migrated, FastEndpoints packages/runtime are removable.

---

## Stage 6: Native Validation, Problem Details, OpenAPI, and Contract Generation ✅

### Goal

Make native ASP.NET Core metadata the authoritative contract and remove FastEndpoints/FluentValidation/Swagger coupling.

### Native validation migration

Replace existing FluentValidation rules using native validation mechanisms without weakening behavior.

Known examples to preserve include:

- update-user availability:
  - exactly seven days;
  - allowed/unique day names;
  - conditional start/end times;
  - valid ranges;
  - vacation range validation;
- appointment creation:
  - conditional/cross-property recurrence rules;
- password reset:
  - OTP/recovery-code mutual exclusivity;
- client update:
  - nested vacation validation;
- password policy:
  - required/length/regex rules;
  - common-password lookup through a dedicated service rather than embedding file/memory-map mechanics into DTO attributes.

Current validators do not require async DB/API validation; do not add an async validation framework without a concrete need.

### Problem Details

- register `AddProblemDetails()` / `IProblemDetailsService`;
- define one stable external error shape;
- add canonical W3C `traceId` centrally;
- preserve validation field errors;
- preserve conflict/idempotency/security response semantics;
- ensure unhandled exceptions become safe Problem Details without leaking internals.

### Pagination cleanup

Keep pagination metadata in the JSON body. Standardize on a clearer contract such as:

```json
{
  "items": [],
  "page": {
    "page": 1,
    "pageSize": 20,
    "total": 0
  }
}
```

Migrate existing `data/info` naming to `items/page` as part of the contract regeneration.

### Native OpenAPI

- add the native ASP.NET Core OpenAPI package/configuration appropriate to .NET 10;
- OpenAPI 3.1;
- operation IDs come from generated endpoint names;
- add document/operation/schema transformers only for metadata native inference cannot express directly;
- reproduce useful existing metadata:
  - auth/security requirements;
  - Problem Details responses;
  - idempotency headers;
  - descriptions/summaries;
  - common response metadata;
- Development only:
  - `MapOpenApi()`;
  - Scalar API reference UI;
- Production:
  - no runtime API-document endpoint/UI.

### Build-time contract generation

- add build-time API description generation;
- explicitly skip design-time/restore/recursive generation contexts;
- generate temporary OpenAPI JSON;
- run Kiota;
- write generated client/models into the committed frontend generated-source directory;
- run fast TS typecheck;
- fail build on any generation/typecheck failure.

### Remove after completion

- FastEndpoints;
- FastEndpoints.Swagger;
- FluentValidation packages/usages that are no longer needed;
- old Swagger/NSwag customization code.

### Acceptance criteria

- every API operation has a stable non-empty OpenAPI `operationId`;
- generated OpenAPI fully describes request/response DTOs needed by Kiota;
- generated client compiles without handwritten API DTO mirrors;
- Development Scalar works;
- Production has no OpenAPI/Scalar route;
- root `dotnet build` regenerates client in-place;
- CI catches stale committed generated code.

---

## Stage 7: Kiota Transport, Frontend API Migration, and Reliable Session Refresh ✅

### Goal

Replace Axios while preserving and hardening all existing session behavior. This stage **absorbs the old roadmap stage “Reliable Access-Token Refresh After Inactivity”; do not leave that work for later.**

### Transport architecture

Use one configured application-wide Kiota Fetch transport stack:

```text
MelodyTrackAuthenticationProvider
        ↓
SessionRefreshMiddleware
        ↓
other narrow middleware as required
        ↓
FetchRequestAdapter / fetch
```

Responsibilities:

- authentication provider adds the current in-memory bearer access token;
- session refresh middleware handles response-aware `401` recovery;
- exactly one shared refresh promise/operation per browser auth context;
- concurrent failed requests wait for the same refresh;
- retry the original request exactly once after successful refresh;
- refresh endpoint bypasses refresh-on-401 recursion using an explicit request option/middleware flag or another narrow mechanism;
- cookie credentials are sent where required;
- CSRF header is added specifically to cookie-authenticated refresh/logout operations;
- preserve `AbortSignal`/cancellation from application call to fetch;
- preserve custom request headers such as idempotency keys;
- preserve blob/download behavior;
- normalize Kiota/HTTP errors into `AppError`.

### Remove legacy auth transport

- remove Axios interceptors/client infrastructure;
- remove legacy refresh token from `localStorage`/request-body migration path;
- remove generic CSRF-on-every-mutation behavior;
- remove duplicate handwritten request/response API DTOs as generated models replace them.

### Inactivity and resume behavior

Preserve/implement the useful scope from the old Stage 18:

- a suspended/backgrounded tab with an expired access token but valid refresh session resumes transparently;
- refresh occurs on the first protected request when needed;
- do not depend on a background timer firing while the page is suspended;
- optionally trigger a proactive validity check on `visibilitychange`, `pageshow`, focus, or connectivity restoration only if it materially improves UX;
- concurrent requests do not rotate the refresh token several times;
- multi-tab behavior must not trip replay protection or incorrectly erase a recoverable session;
- temporary network/backend failures are not treated as terminal auth expiry;
- terminal expired/revoked/invalid refresh state clears auth once and leads to an actionable login/session entry point;
- no original state-changing request is duplicated by retry loops;
- logout/credential-recovery/2FA/session-revocation in another tab is handled deterministically;
- browser clock changes and backend restart do not cause unnecessary destructive auth state changes.

### Application-facing wrappers

Keep semantic wrappers such as:

```text
clientsApi.list(...)
clientsApi.create(...)
appointmentsApi.reschedule(...)
```

These wrappers:

- expose application-oriented method names;
- attach idempotency/cancellation/options where needed;
- hide raw Kiota builder ceremony;
- return generated DTOs or frontend-owned view models;
- remain owned by the appropriate FSD slice.

### Error UI migration

- replace the current “toast-only” global query failure behavior for important errors;
- update `QueryStateBlock`, `ListQueryStatus`, forms/modals, and mutation helpers to receive normalized errors rather than booleans/generic strings;
- add persistent contextual error rendering;
- add global persistent fallback;
- add `CopyTraceIdButton`/generic text-copy primitive;
- update the clipboard-boundary security check to recognize the reviewed non-URL trace-ID copy case.

### Acceptance criteria

- Axios is no longer used for MelodyTrack API transport;
- all API access runs through one Kiota adapter/session stack;
- concurrent `401`s cause one refresh and one replay per original request;
- long-inactive valid sessions recover without page reload;
- network failure does not masquerade as logout;
- terminal refresh failure clears the correct auth context only once;
- generated models compile through all migrated API wrappers/UI;
- meaningful backend failures display the canonical trace ID persistently.

---

## Stage 8: Authentication, Authorization, Portal, and Crypto Cutover ✅

### Goal

Perform the breaking authentication/security migration after the native API/client stack is stable.

### ES256 JWT migration

- replace symmetric JWT signing with ES256/P-256;
- use dedicated signing private key material;
- validate only the intended algorithm;
- configure explicit issuer and audience;
- validate signature/lifetime/issuer/audience;
- continue approximately 10-minute access-token lifetime;
- roles remain out of the JWT;
- session ID/identity claims remain sufficient for server-side active-session lookup.

### Secret separation

Provision independent production secrets:

- JWT private key;
- password pepper;
- portal PIN pepper;
- refresh-token hash key;
- CSRF signing key.

Validate their presence/format/entropy at startup/Init as appropriate.

Do not derive them from one master secret.

### Password/PIN cutover

Because production currently has very few staff users and portal PIN usage is minimal:

- do not add normal-runtime legacy-pepper compatibility;
- revoke all existing sessions at the migration boundary;
- mark/clear existing staff password credentials so password reset is required;
- clear legacy portal PIN hashes so clients set a new PIN on next valid portal-link use;
- provide an explicit server-local recovery/bootstrap command that can issue a reset path for the first superuser without relying on another logged-in admin;
- after one superuser recovers, ordinary existing reset/admin flows may be used for other staff;
- do not log raw reset credentials except through an explicitly requested secure recovery output mode.

### Password/PIN hashing

- Argon2id;
- current-strength parameters selected and documented at implementation time;
- dedicated peppers;
- hash format/version allows future rehash detection;
- common-password validation remains available without logging submitted passwords.

### Authorization policy migration

- introduce DB-backed coarse policies;
- remove repeated manual role gates where policy metadata is sufficient;
- preserve resource-level checks;
- ensure role changes are effective from DB state;
- ensure active-session revocation is checked independently.

### Remove dormant account lockout

- remove `User.LockedUntil` and its DB column/migration state if repository-wide inspection confirms no remaining meaningful use;
- do not implement permanent account lockout;
- audit/improve ASP.NET rate-limit policies for login, password recovery/reset verification, invite/OTP/recovery-code operations, and other brute-forceable anonymous credentials.

### Portal fixes

- keep permanent reusable portal link semantics;
- migrate portal token hashing to the appropriate dedicated hash/key design if still tied to old shared auth material;
- keep raw token out of logs;
- add DB-backed escalating PIN cooldown;
- normal portal login creates an additional device session rather than revoking all other client sessions;
- portal security operations can still revoke all relevant sessions;
- fix session lifetime so portal refresh remains on the portal sliding window rather than accidentally becoming staff 7-day duration;
- keep one renewable identity per browser profile.

### Session metadata cleanup

- if the existing session DTO calls a creation timestamp `LastSeenAtUtc` without actual activity tracking, rename it to `CreatedAtUtc` rather than adding write-on-every-request activity updates solely to justify the old name;
- preserve useful device/session information for session-management UI;
- avoid adding IP/device binding as an authentication requirement.

### CSRF cleanup

- dedicated CSRF key;
- CSRF required for cookie-authenticated refresh/logout;
- remove redundant generic mutation header behavior;
- retain signed/session-bound validation and constant-time comparison.

### Acceptance criteria

- old HMAC JWTs and old sessions no longer authenticate after cutover;
- recovered staff can sign in with newly hashed Argon2id credentials;
- portal clients with valid links can establish new PINs;
- normal staff/portal refresh is sliding with the correct duration;
- portal multi-device sessions coexist;
- role authorization is policy-based and DB-current;
- brute-forceable anonymous auth endpoints have reviewed rate limits;
- no dormant `LockedUntil` behavior remains;
- no production auth secret is reused for an unrelated purpose.

---

## Stage 9: Backend and Init Observability ✅

### Goal

Make Backend and Init diagnosable without adding a large observability stack.

### Backend/Init instrumentation

- instrument Backend and Init with OpenTelemetry;
- distinct service names, e.g. `melodytrack-backend` and `melodytrack-init`;
- same release `service.version`;
- environment/resource attributes as appropriate;
- console/Serilog output remains sufficient if telemetry export is unavailable;
- telemetry failure never causes Init/application failure.

Init trace/log boundaries should cover meaningful steps such as:

- EF migrations;
- data backfills;
- Quartz initialization;
- PII key/version verification/re-encryption;
- Development seed upgrades;
- production bootstrap/recovery state.

### Npgsql/EF telemetry

- enable traces/metrics;
- production command text policy as described in the architecture section;
- no production parameter values/sensitive-data logging.

### Canonical trace IDs

- update `X-Trace-Id` to W3C trace ID;
- central Problem Details `traceId` matches;
- test incoming `traceparent` propagation;
- test errors/conflicts/unhandled failures retain one trace identity.

### Explicit scope boundaries

- frontend/browser telemetry and OTLP relay endpoints are not part of this refactor;
- the frontend continues to display and copy trace IDs returned by Backend errors;
- production Dashboard deployment, security, retention, and operator procedures are documented in `docs/production-telemetry.md`, while the deployment artifacts remain in the external infrastructure stack;
- the repository owns only Backend/Init instrumentation and standard OTLP exporter configuration.

### End-to-end support scenario

Create a deterministic manual/integration scenario:

1. trigger a backend error from the UI;
2. UI shows persistent error + trace ID;
3. copy trace ID;
4. search the development Aspire Dashboard or an externally managed production telemetry backend;
5. locate the same trace;
6. confirm API/backend/DB correlation where instrumentation applies.

### Acceptance criteria

- Backend and Init telemetry exports when an OTLP endpoint is configured;
- trace IDs match response header/ProblemDetails/logs;
- no browser telemetry or relay is shipped;
- telemetry exporter outage does not break MelodyTrack;
- production Dashboard operations follow `docs/production-telemetry.md`, with deployment artifacts kept outside the application repository.

---

## Stage 10: Test Infrastructure, Release Automation, and Refactor Cutover ✅

### Goal

Remove transitional infrastructure, prove the unified architecture, and return the repository to a stable product-development state.

### Test migration

- replace `FastEndpoints.Testing.AppFixture<Program>` with standard `WebApplicationFactory<Program>` where applicable;
- every integration database starts from PostgreSQL Testcontainer;
- run actual `MelodyTrack.Init --mode test` before Backend;
- retain controlled-time/concurrency tests for sessions, recurrence, idempotency, etc.;
- add focused tests for source-generator-enforced endpoint shape through ordinary compilation/build failures where practical;
- keep browser/WebKit tests working under the monorepo/unified runtime.

### Required integration coverage

At minimum cover:

- Init success/failure gating Backend;
- SPA fallback boundaries;
- security/cache/compression headers;
- `/health` and `/alive` behavior;
- native validation and Problem Details;
- native auth/authorization policies;
- refresh rotation/replay/concurrency;
- inactivity resume;
- portal multi-device login + PIN cooldown;
- canonical trace correlation;
- generated API client freshness;
- PII key-version initialization failure/migration;
- Quartz initialization/startup boundary.

### Release-tool cleanup

- complete monorepo `ReleaseTool` simplification;
- remove obsolete frontend release workflow script;
- implement/verify one-file changelog operations;
- implement automatic version allocation and next-draft creation;
- verify hotfix flow and merge-back semantics;
- make publish CI the normal path after valid release/hotfix merge to `master`;
- keep manual publish only as idempotent recovery if useful.

### Remove all transitional/dead infrastructure

Remove once replacements are proven:

- FastEndpoints;
- FastEndpoints.Swagger/NSwag-specific document code no longer required;
- FluentValidation;
- Axios API transport;
- frontend nginx production image/container/config;
- old CORS configuration;
- old Swagger UI/runtime route;
- SerilogTracing tracing layer;
- old sibling-repository release coordination;
- legacy browser refresh-token migration code;
- dead account-lockout fields/code;
- duplicate handwritten TS API DTOs;
- obsolete nginx-specific security checker.

### Query-efficiency cleanup

- replace the remaining per-service latest-price reads in service list mapping with one batched or projected query;
- consolidate recurring-task candidate evaluation so rules reuse shared debtor, appointment, and payment datasets instead of rerunning the same query bundles;
- select the latest audit activity per entity in the database instead of loading the complete matching history and grouping it in memory;
- batch development demo-data existence and lookup checks that currently issue per-record reads inside seeding loops;
- add bounded database-command coverage for affected list and batch endpoints, and explicitly document any intentionally row-oriented Init migrations that cannot be safely batched.

### Refactor exit criteria

The refactor program is complete only when all of the following are true:

- one monorepo;
- root `dotnet build` performs the agreed cross-stack contract build;
- project-level builds remain scoped;
- `dotnet publish` produces a complete API+SPA artifact;
- one production MelodyTrack container;
- Init runs before Backend in production/dev/test as appropriate;
- Aspire AppHost provides the complete development environment;
- all API endpoints are native Minimal APIs registered through the compile-time endpoint convention;
- native validation/OpenAPI/ProblemDetails replace FastEndpoints-specific equivalents;
- Kiota + Fetch replace Axios and generated DTOs are authoritative;
- reliable inactivity refresh behavior is complete;
- ES256 + separated secrets + Argon2id cutover is complete;
- portal session/cooldown corrections are complete;
- DB-backed authorization policies are complete;
- versioned AES-GCM PII migration path is complete;
- Backend/Init OpenTelemetry export path is operational;
- release tooling/CI operates as one application/repository;
- identified N+1 and repeated-query hotspots are eliminated or explicitly bounded and documented;
- required integration/browser verification is green;
- production deployment documentation matches the new runtime;
- there are no intentionally retained “temporary migration” implementations that should have been removed.

---

# Post-Refactor Product Roadmap

All stages below are product work and begin after the refactor exit criteria above.

## Stage 11: God Mode and System Notices

### Goal

Add an out-of-band god mode that remains usable when normal MelodyTrack authentication is unavailable, and add a real application notice system for communicating administrative/security events to users.

This is a new feature, not part of the refactor.

### God mode trust model

Do not create a permanent god mode username/password.

Use direct server access as the root of trust:

```text
SSH / direct server access
        ↓
server-local command
        ↓
short-lived one-time god mode login token
        ↓
LAN-only HTTPS god mode
        ↓
short-lived god mode session
```

Recommended properties:

- one-time token generated from cryptographically secure randomness;
- token valid for only a short window, e.g. ~5 minutes;
- persist only a hash if persistence is required;
- consuming it invalidates it;
- exchange creates a short-lived `HttpOnly`, `Secure` god mode session, e.g. ~30 minutes;
- normal MelodyTrack user/session credentials cannot access god mode;
- merely being on the LAN is not sufficient authentication;
- god mode authentication does not depend on user password/2FA tables being healthy.

### Network/runtime isolation

- expose the god mode API/UI through a separate Kestrel listener/port or another clearly isolated endpoint surface;
- Caddy routes a dedicated god mode hostname to that listener;
- Caddy restricts the hostname to LAN ranges;
- keep the god mode listener off the public application route;
- do not expose it directly from Docker to the Internet;
- no need for mTLS initially unless the server-token model proves insufficient.

### Minimal server-local CLI

The CLI is primarily an access bootstrap, not a duplicate god mode implementation.

Initial commands may include:

```text
melodytrack god-mode
```

Exact executable packaging may follow the repository's CLI/tooling conventions.

Add direct recovery CLI commands only when a real emergency workflow cannot reasonably use god mode.

### God mode capabilities

Initial scope:

- inspect users and current credential/session state;
- force password reset state;
- generate/revoke one-time password reset links;
- revoke one session;
- revoke all sessions for a user;
- reset client portal PIN;
- rotate/revoke permanent client portal link;
- inspect bootstrap/invite recovery state;
- create/update/expire/delete system notices;
- create a safe global pre-auth/login-page announcement.

Explicit non-goals:

- no arbitrary SQL console;
- no arbitrary DB editing;
- no user impersonation;
- no shell execution;
- no arbitrary business-record mutation;
- no runtime cryptographic-key editing/rotation UI.

### System notices

Add a dedicated user-facing notice model rather than encoding operational actions as messages.

Suggested concepts:

```text
SystemNotice
- Id
- Title
- Body
- Severity
- CreatedAtUtc
- ExpiresAtUtc?
- Dismissible
- AudienceType
```

Audience support should cover at least:

- everyone;
- staff;
- clients;
- specific users/recipients.

Use a join/read-state model as required for per-recipient dismissal/read state.

Notice UI:

- persistent banner/inbox-style rendering;
- clear severity;
- optional expiry;
- dismissibility enforced by the model;
- no sensitive information in a public pre-auth notice.

### Enforcement vs communication

Never use a notice as the enforcement mechanism.

Examples:

- “password reset required” is a backend credential state + optional notice;
- “sessions terminated” is an actual session revocation + optional notice;
- portal PIN reset is actual credential mutation + optional notice.

### Audit

Every god mode action records an audit event with:

- god mode session reference;
- action type;
- target reference;
- timestamp;
- result;
- trace ID.

Never audit/log raw god mode tokens, reset tokens, passwords, PINs, refresh tokens, or permanent portal tokens.

### Done looks like

- a normal MelodyTrack auth outage does not lock the server owner out of essential recovery operations;
- god mode cannot be reached from the public Internet;
- normal superuser credentials cannot authenticate to it;
- one-time god mode tokens cannot be reused;
- credential reset/session revocation actions are enforced server-side;
- users can receive persistent global or targeted notices;
- all privileged god mode actions are auditable without leaking credentials.

---

## Stage 12: Notification Infrastructure and Web Push

### Goal

Create a reusable notification subsystem for workflow events. In-app notification state is authoritative; Web Push is an optional best-effort delivery channel.

This stage exists primarily to support appointment-rescheduling requests but should remain general enough for later application workflows.

### In-app notification model

Add a first-class notification model, conceptually:

```text
Notification
- Id
- RecipientPrincipalType
- RecipientPrincipalId
- Type
- Title/summary or localized payload key
- Safe payload/reference data
- DeepLink?
- CreatedAtUtc
- ReadAtUtc?
- ExpiresAtUtc?
```

Use a normalized type/payload design that does not require putting arbitrary HTML or sensitive entity snapshots into notification rows.

Support at least:

- staff/admin recipients;
- client-portal recipients.

### API/UI

- list unread/recent notifications;
- unread count/badge;
- mark read;
- mark all read where useful;
- deep-link to the owning workflow/entity;
- persistent history for a reasonable product-defined period;
- notifications remain visible even if push delivery fails.

### Web Push

Implement Web Push where the browser/platform supports it.

Requirements:

- separate VAPID key pair;
- VAPID private key is a production secret;
- public key may be exposed to the browser as required by the protocol;
- push subscription belongs to an authenticated principal/session/account context;
- subscription can be revoked/removed;
- clean up subscriptions rejected as permanently invalid by the push service;
- permission prompt is user-initiated/contextual, not an immediate first-page nag;
- provide clear fallback when the platform/browser does not support push;
- push message content is privacy-minimized, e.g. “Your appointment request was processed” rather than client/service details;
- push deep-links into the authenticated app for details;
- push delivery success is never required for workflow correctness.

A service worker may be introduced for push handling, but do **not** use this stage to silently implement the later offline-first architecture. Keep caching/offline behavior minimal unless separately required for correct push operation.

### Delivery behavior

- write the in-app notification transactionally or reliably as part of the business workflow;
- enqueue/attempt push after the durable notification exists;
- push failures are observable/retriable according to a bounded policy;
- do not roll back the business operation because a push provider/browser endpoint is unavailable.

### Observability

Track useful low-cardinality metrics/logs:

- notifications created;
- push attempted/succeeded/failed;
- invalid subscriptions removed;
- delivery latency where useful.

Do not emit payload PII into metrics/logs.

### Done looks like

- admins and clients have durable in-app notifications;
- unread/read state works across reloads;
- supported browsers can opt into Web Push;
- unsupported/denied push does not degrade core workflow visibility;
- push contains no unnecessary sensitive details;
- notification creation and push delivery failures are observable.

---

## Stage 13: Client Appointment Rescheduling Requests

### Goal

Allow a client to request a new time for the **next eligible future appointment visible in the client portal**, without exposing other clients' calendar data or letting the client change the assigned teacher/provider.

Administrators review the request. Accepting the request automatically reschedules the single appointment occurrence after re-validating availability. Declining it records the decision. The client receives durable confirmation and, where available, Web Push.

### Product rules

- client can request rescheduling only for the next eligible future appointment exposed by the portal;
- teacher/provider cannot be changed;
- service/appointment identity remains the same; this is a time change, not a replacement appointment;
- duration remains the appointment's existing duration unless existing business rules already define otherwise;
- for a recurring appointment, this workflow changes **only the selected materialized occurrence**;
- do not modify the recurrence pattern or future occurrences through this client workflow;
- recurrence rematerialization must preserve the accepted per-occurrence exception using the project's existing recurrence exception semantics;
- at most one active pending reschedule request per appointment;
- do not invent a minimum-notice/deadline rule unless existing scheduling rules already provide one or product requirements later specify it.

### Privacy-safe teacher calendar

Add a dedicated client-safe availability contract. Do **not** reuse a rich staff calendar DTO and merely hide selected fields.

The client may see only enough information to choose a viable time, e.g. intervals/status such as:

- `Available`;
- `Busy`/unavailable;
- `Vacation`;
- `Weekend`/outside working schedule.

Never expose through this contract:

- other client names/IDs;
- service names/types for other appointments;
- appointment notes;
- prices/payments;
- staff-only status/details;
- any unnecessary identifier that lets a client correlate another person's appointments.

The calendar is fixed to the appointment's existing teacher/provider.

Availability generation must use the same authoritative scheduling rules as normal staff booking so the portal does not advertise slots the backend would immediately reject.

### Request entity/state machine

Create a first-class request entity, not a notification-only payload.

Suggested fields/concepts:

```text
AppointmentRescheduleRequest
- Id
- AppointmentId
- ClientId
- Teacher/UserId (immutable snapshot/reference for authorization)
- OriginalStart/End
- RequestedStart/End
- Status: Pending | Accepted | Declined
- CreatedAtUtc
- ProcessedAtUtc?
- ProcessedByUserId?
- DeclineMessage?
- concurrency/version field as appropriate
```

If appointment state can materially change while a request is pending, store enough original appointment/version information to detect stale requests safely.

### Client workflow

1. Client opens next appointment.
2. Client selects “Request another time”.
3. Portal loads privacy-safe availability for the fixed teacher.
4. Client chooses a valid slot.
5. Backend re-validates basic eligibility and creates a `Pending` request.
6. UI immediately confirms that the request was submitted and shows its current status.
7. Administrators receive an in-app notification and Web Push where subscribed.
8. Client can continue to see pending/processed status in the portal.

Do not imply that submission has changed the appointment.

### Administrator workflow

Provide a staff/admin review surface showing:

- client/appointment identity that the administrator is already authorized to see;
- original appointment time;
- requested time;
- current slot availability;
- request age/status;
- Accept;
- Decline;
- optional short decline message.

### Accept transaction

Acceptance must be transactional and must re-check current state at processing time:

1. request exists and is still `Pending`;
2. appointment still exists and is eligible;
3. appointment/teacher relationship still matches the request;
4. requested slot is still valid under working hours/weekends/vacations/business rules;
5. requested slot is still free and does not create a collision;
6. appointment has not been changed in a way that invalidates the request;
7. for recurring appointments, update only the concrete occurrence and preserve recurrence exception behavior;
8. write appointment change + request `Accepted` state + audit event atomically where practical;
9. create client notification after/during the durable transaction through the notification infrastructure.

If the slot is no longer available:

- do not double-book;
- do not silently move to another slot;
- fail the Accept action with a clear conflict;
- keep the request pending unless the administrator explicitly declines it, so the decision remains intentional.

### Decline flow

- request must still be pending;
- mark `Declined`;
- optional short administrator message;
- do not change the appointment;
- create client in-app notification;
- send privacy-safe push where possible.

### Client confirmation

Processed requests remain visibly confirmed in the portal even when push is unavailable.

Accepted example:

```text
Your rescheduling request was accepted.
New appointment: <date/time>
```

Declined example:

```text
Your rescheduling request was declined.
<optional administrator message>
```

### Authorization and abuse controls

- client can read/create requests only for their own visible appointment;
- client cannot substitute another teacher/appointment ID;
- admin processing endpoints use staff/admin policy as appropriate;
- request creation is rate-limited enough to prevent trivial spam without making ordinary use annoying;
- duplicate pending requests for the same appointment are rejected/returned idempotently as appropriate;
- all state transitions are audited.

### Testing

Backend/integration tests:

- next-appointment eligibility;
- privacy-safe calendar serialization;
- vacation/weekend/busy/free calculations;
- cross-client access denial;
- teacher cannot be changed;
- duplicate pending request handling;
- accept happy path;
- accept race where slot becomes occupied;
- accept stale appointment state;
- decline;
- recurring appointment changes one occurrence only;
- future recurrence remains unchanged;
- notification creation;
- audit events.

Frontend/browser tests:

- client can discover/request a free slot;
- no other-client details appear in UI/network contract;
- pending state is clear;
- admin receives/reviews request;
- accepting updates appointment UI;
- declining shows client confirmation;
- push unsupported/denied still leaves durable in-app result.

### Done looks like

- a client can request a new time without contacting staff through another channel;
- the client never sees sensitive details from the teacher's calendar;
- staff retains final control;
- accepting cannot double-book an appointment;
- recurring series are not accidentally rewritten;
- client receives durable processed-state confirmation;
- push improves timeliness but is never required for correctness.

---

## Stage 14: Services Progress

MelodyTrack should track structured learning progress alongside scheduling. Services remain appointment and billing concepts; courses represent a client's long-term learning path.

### Remaining scope

- Reconsider and approve the client-facing course-progress experience before restoring it to the portal. Use [the course progress map brief](docs/course-progress-map-brief.md) as design context, not as an implementation checklist.
- Keep the client schedule available independently from course progress.
- Harden enrollment, dependency, unlock, completion, points, audit, and template-evolution behavior for daily use.
- Add focused backend and frontend verification for course assignment, appointment-to-theme linkage, progress transitions, and authorization.
- Improve explanations and recovery paths for blocked themes, invalid graphs, and stale enrollment state.

### Deferred product work

- Client-facing course progress and shared theme-content access remain deferred until their visual and usability direction is approved.
- Chat, homework uploads, automated homework checking, feedback threads, achievements, leaderboards, marketplace features, and inline media attachments are outside this stage.
- Completion remains teacher-controlled; do not infer it from appointment count, notes, or homework submission alone.

### Integration with the refactored architecture

- new/changed APIs use generated Minimal API registration and Kiota DTOs;
- authorization uses the centralized policy model plus resource ownership checks;
- errors use Problem Details/AppError/trace-ID UI;
- notifications may be used when a real workflow benefit exists, but do not turn every progress state change into push noise;
- preserve auditability and OTel visibility for important failures.

### Done looks like

- Staff can run course enrollment and progress without fragile manual workarounds.
- Progress rules and point changes are explicit, auditable, and covered by tests.
- Clients can access only their own approved portal surfaces.
- Any restored client course-progress UI is understandable and useful in normal teaching work.

---

## Stage 15: Multiple Staff Accounts in One Browser

Allow the main staff portal to remember several staff user accounts in one browser and switch between them without repeatedly entering full credentials. This stage intentionally changes the refactor-era “one renewable identity per browser profile” limitation for **staff accounts only**.

Client-portal identities remain a separate experience and are not part of the staff account switcher.

### Scope

- Define the server/browser session model needed to keep several staff accounts available simultaneously.
- The single refresh-cookie model must no longer silently choose one active staff account; introduce an explicit revocable session/account identity or another server-backed switching model.
- Do not persist raw access tokens, refresh tokens, passwords, 2FA secrets, or other reusable credentials in Local Storage/IndexedDB.
- Store only chooser metadata such as stable account reference, display name, role summary, avatar, and last-used time.
- Add account chooser/login/switching UI.
- Support:
  - add another staff account;
  - switch active account;
  - remove one remembered account from this browser;
  - distinguish removing an account from logout-all/server-session revocation.
- Define when switching is immediate and when password/2FA reauthentication is required.
- Expired, revoked, disabled, or permission-changed accounts fail closed with actionable recovery.
- Make account identity part of every browser-owned data boundary:
  - TanStack Query cache;
  - reference-label caches;
  - drafts/forms;
  - persisted preferences where identity-specific;
  - IndexedDB/offline data;
  - notification state;
  - cross-tab coordination.
- Clear/partition state before rendering the newly selected account.
- Coordinate switching across tabs without stale tabs reviving a removed session or writing data into the wrong account context.
- Preserve short access tokens, refresh rotation, CSRF, session revocation, audit events, DB-backed authorization, 2FA, notifications, and trace correlation.
- Audit add/switch/remove/revoke events without recording credentials.
- Cover administrators, teachers, superusers, invite completion, password reset, session expiry, disabled users, and role changes while remembered.

### Relationship to reliable refresh

The basic refresh/retry behavior is already implemented during the refactor. This stage extends it to **multiple remembered staff auth contexts** rather than redesigning it from scratch.

Every refresh response/cross-tab event must be scoped to the intended account context.

### Done looks like

- one browser can remember multiple staff accounts and clearly show the active one;
- switching is faster than fresh login but reauthenticates when required;
- no query result/draft/route/permission/notification/offline state leaks between accounts;
- removing one remembered account leaves others usable;
- logout-all revokes the intended server sessions;
- multi-tab refresh/expiry/revocation/2FA/role-change scenarios are covered.

---

## Stage 16: Offline-First Operations Architecture

Design how MelodyTrack can keep the most common daily staff work available during internet outages and infrastructure shutdowns. The target is more than cached read-only pages: authorized staff should be able to create, edit, and delete supported records locally, close the browser if necessary, and synchronize safely when Backend returns.

This is an architecture/product-discovery stage. It must produce a validated design and thin end-to-end prototype before broad implementation. Do not enable offline mutations across all domains until conflict, security, and reconciliation rules are explicit.

### Scope

- Inventory workflows required during outage for clients, schedule, services, payments, and closely related reference data.
- Rank operations by frequency, criticality, conflict risk, and whether they can be made safe without a live server decision.
- Define the first offline release boundary. Prefer one vertical slice proving durable create/edit/delete, restart recovery, sync, and conflict handling.
- Design versioned, account- and organization-scoped IndexedDB stores for:
  - cached read models;
  - mutation commands;
  - temporary-to-server ID mappings;
  - sync checkpoints;
  - conflicts.
- Specify migrations, corruption recovery, quota handling, browser eviction behavior, and explicit local-data reset.
- Represent each offline mutation as a durable idempotent command carrying:
  - owner/account;
  - target;
  - operation;
  - payload version;
  - creation time;
  - dependency order;
  - client-generated idempotency key;
  - expected server version/concurrency token.
- Queue must survive reload and never replay a successful command twice.
- Support temporary IDs/dependency graphs, e.g. appointment referencing a client created earlier in the same offline period.
- Define how later edits/deletes compact/supersede queued commands without changing intended final state.
- Define server sync APIs for:
  - batch sync;
  - partial success;
  - validation errors;
  - idempotent retries;
  - deleted records;
  - concurrent server changes.
- Preserve audit distinction between offline creation time and synchronization time.
- Define conflict policies per domain, not one global last-write-wins rule.
- Explicitly cover schedule collisions, recurrence, service price changes, client merges, and financial records.
- Treat payments conservatively:
  - which operations may be captured offline;
  - whether provisional until sync;
  - duplicate receipt prevention;
  - corrections requiring online/elevated action.
- Design sync coordinator around login, Stage 15 account switching, connectivity changes, app startup, browser background limitations, and manual retry.
- UI must show queue status, unsynchronized changes, conflicts, blocked dependencies, and last successful sync.
- Do not treat `navigator.onLine` as proof Backend is reachable.
- Define offline application shell/asset strategy and service-worker/PWA requirements.
- Define deployment/cache update behavior and prevent obsolete clients from sending incompatible queued commands.
- Perform security/privacy review for data at rest in shared browser profiles:
  - logout;
  - account removal;
  - role changes;
  - session expiry;
  - device loss;
  - browser profiles;
  - local encryption limitations;
  - retention;
  - remote revocation.
- Offline sync must re-check authorization server-side.
- Prototype one representative workflow through outage, browser restart, reconnection, conflict, and successful reconciliation.
- Produce:
  - ADR;
  - data/command schemas;
  - sync protocol;
  - domain support matrix;
  - conflict UX;
  - failure-state matrix;
  - rollout/migration plan;
  - observability requirements;
  - browser test strategy.

### Interaction with Stage 12 push service worker

If a minimal service worker already exists for Web Push, treat it as infrastructure to extend carefully. Do not assume its caching/lifecycle behavior is suitable for offline operations without explicit redesign/versioning.

### Done looks like

- supported and deliberately online-only operations are documented with product rationale;
- a reviewed prototype proves queued create/edit/delete survives reload and synchronizes idempotently without cross-account leakage;
- temporary IDs, dependencies, validation failures, concurrent edits/deletes, schedule conflicts, and partial sync have explicit rules/recovery UX;
- security, storage durability, browser support, deployment compatibility, auditability, and financial integrity risks have accepted mitigations;
- implementation can be split into safe vertical releases rather than one all-or-nothing rewrite.

---

## Stage 17: Accounting and Staff Compensation Architecture

Define how accounting should integrate with MelodyTrack's existing services, appointments, payments, expenses, users, and statistics. The design must cover staff salary calculation/payment while keeping scheduling, cash movement, earned revenue, expenses, payroll liabilities, and actual payouts as distinct concepts.

This is an accounting-domain discovery/architecture stage. Validate the model with the people who will reconcile the numbers before building UI or automating salary calculations.

### Scope

- Document the accounting questions MelodyTrack must answer:
  - money received;
  - money owed by clients;
  - revenue earned;
  - operating expenses;
  - compensation accrued to each user;
  - compensation paid;
  - outstanding payroll liabilities;
  - organization profit for a period.
- Agree on accounting basis and period rules:
  - cash vs accrual reporting;
  - timezone/closing boundaries;
  - supported currencies;
  - rounding;
  - refunds;
  - prepayments;
  - partial payments;
  - debt;
  - cancellations/burned lessons;
  - deleted appointments;
  - corrections to closed periods.
- Model effective-dated compensation agreements per user.
- Evaluate fixed salary, per-appointment/per-hour rates, percentage of service revenue, role/service-specific rates, bonuses, deductions, guarantees, and mixed schemes without assuming every organization uses every option.
- Define exactly when compensation is earned:
  - planned;
  - completed;
  - paid by client;
  - burned;
  - manually approved;
  - or another explicit rule.
- Cover recurring appointments, substitutions, multiple staff members, changed service prices, changed compensation agreements, vacations, and non-appointment work.
- Preserve historical truth by snapshotting the calculation inputs/rule used for each accrual.
- Later edits to users/services/prices/appointments/agreements must not silently rewrite approved historical payroll.
- Separate:
  - calculated accruals;
  - manual adjustments;
  - approvals;
  - pay runs;
  - payout transactions.
- Define draft/reviewed/approved/paid/voided/corrected states with immutable history and reversal entries after approval rather than destructive edits.
- Decide how existing payments/expenses map into a ledger/accounting journal.
- Specify stable references/reconciliation so salary payout is not double-counted as both unexplained expense and payroll transaction.
- Design permissions/separation of duties for:
  - viewing compensation;
  - editing agreements;
  - entering adjustments;
  - approving payroll period;
  - marking payouts;
  - reopening periods;
  - exporting accounting data.
- Salary information must not leak through general user/statistics APIs.
- Define administrator workflows for setup, previews, exception review, approval, payout recording, corrections, and per-user statements.
- Every calculated amount must expose a human-readable breakdown to source appointments/rates/bonuses/deductions/adjustments.
- Define reports/reconciliation invariants before UI:
  - cash flow;
  - receivables;
  - revenue;
  - expenses;
  - payroll accruals;
  - payroll paid;
  - liabilities;
  - profit.
- Establish the boundary between MelodyTrack internal management accounting and statutory payroll/tax/banking/invoicing/jurisdiction-specific compliance.
- Unsupported legal/statutory responsibilities must be clearly labeled/exported for an external accounting system rather than presented as complete payroll compliance.
- Evaluate imports/exports, external identifiers, duplicate detection, period locking, audit events, retention, backup/restore, and migration for existing payments/expenses without fabricating historical detail never recorded.
- Produce:
  - domain glossary;
  - event-to-entry mapping;
  - compensation rule model;
  - permission matrix;
  - report formulas;
  - reconciliation examples;
  - API boundary;
  - staged delivery plan;
  - representative acceptance scenarios reviewed by a domain stakeholder.

### Done looks like

- revenue, cash receipts, receivables, expenses, salary accruals, liabilities, payouts, and profit each have one documented meaning/reconciliation rule;
- representative fixed/hourly/per-appointment/percentage/bonus/deduction/substitution/cancellation/refund/mid-period-rate-change scenarios produce explainable expected results;
- approved periods preserve historical calculations and corrections remain traceable through adjustments/reversals;
- permissions protect salary data and prevent one user from silently changing agreements, approving payroll, and erasing resulting audit history;
- boundary between MelodyTrack accounting and external statutory responsibilities is explicit;
- a reviewed incremental implementation plan can add useful accounting capabilities without breaking current payment/expense/statistics behavior.

---

## Stage 18: Calendar Workflow and Income Forecast Improvements

### Goal

Address the latest customer feedback around trial lessons, calendar workflow, schedule visibility, and forward-looking income.

### Scope

- render trial lessons on the calendar with a distinct color that is not reused by ordinary appointment states;
- add the missing recurring-task reminder for trial lessons;
- allow vacations to include a start and end time instead of being date-only;
- add a calendar shortcut for creating a vacation by dragging time slots, with an interaction that does not conflict with drag-to-create appointments;
- when a superuser opens the calendar, select that user's calendar automatically while continuing to allow selection of other users;
- show administrators the full schedule;
- continue showing teachers only their own schedule, without a control or API path for switching to another user's calendar;
- add forecast income for the selected date/time range to the income statistics page, calculated from planned appointments in that range.

### Done looks like

- trial lessons are immediately distinguishable from ordinary appointments in every supported calendar layout;
- trial-lesson reminders are generated once at the intended time and follow the existing recurring-task deduplication, cancellation, and delay rules;
- timed vacations persist, render, and enforce availability using their actual time range, while existing date-only vacation data has an explicit migration/default interpretation;
- users can deliberately choose between creating an appointment and creating a vacation from calendar time slots without accidental cross-triggering on desktop or mobile;
- calendar defaults and user-selection controls match the superuser, administrator, and teacher rules above, with authorization enforced server-side;
- income statistics clearly separate forecast income from realized income and calculate the forecast only from planned appointments inside the selected range;
- integration and browser tests cover role visibility, calendar gestures, trial-lesson presentation/reminders, timed-vacation boundaries, and forecast calculations.

---

# Dependency summary

Implement in this order unless a concrete blocker requires a documented deviation:

```text
Stage 1  Monorepo baseline
   ↓
Stage 2  Core/Data/Init/config boundary
   ↓
Stage 3  Aspire development environment
   ↓
Stage 4  Unified publish/runtime
   ↓
Stage 5  Minimal API + endpoint generator
   ↓
Stage 6  Native validation/OpenAPI/Kiota generation
   ↓
Stage 7  Kiota transport + reliable refresh + error UX
   ↓
Stage 8  ES256/auth/portal/security cutover
   ↓
Stage 9  Backend + Init OpenTelemetry
   ↓
Stage 10 Test/release cleanup and refactor exit
   ↓
────────────────────────────────────────────
POST-REFACTOR PRODUCT WORK
   ↓
Stage 11 God mode + system notices
   ↓
Stage 12 Notification infrastructure + Web Push
   ↓
Stage 13 Client appointment rescheduling requests
   ↓
Stage 14 Services progress
   ↓
Stage 15 Multiple staff accounts in one browser
   ↓
Stage 16 Offline-first architecture
   ↓
Stage 17 Accounting/staff compensation architecture
   ↓
Stage 18 Calendar workflow + income forecast improvements
```

The ordering among Stages 14-18 may later be changed for product priority, but they remain **after the refactor**. Stage 13 depends on Stage 12. Stage 15 should precede broad offline account-scoped persistence because offline storage must understand final account identity boundaries.

---

# Explicit non-goals for the refactor

Do not add these merely because the architecture is changing:

- production Aspire AppHost orchestration;
- Kubernetes/k3s migration;
- Redis/Valkey dependency;
- multi-replica Backend/Quartz clustering;
- repository/unit-of-work abstraction over EF Core;
- `MelodyTrack.Application` layer;
- general-purpose plugin/event bus architecture;
- API route versioning;
- backward DB compatibility with old releases;
- online multi-key crypto rotation/key rings;
- public Aspire Dashboard;
- Prometheus/Loki/Tempo stack;
- PostgreSQL server-log ingestion;
- frontend/browser telemetry, OTLP relay endpoints, session replay, and source-map symbolication;
- parallel staff+portal browser identities;
- multi-staff-account browser switching before Stage 15;
- offline mutation support before Stage 16;
- Web Push as a prerequisite for any business operation;
- client ability to change teacher/provider in rescheduling;
- recurrence-series changes through client rescheduling;
- god mode user impersonation/arbitrary DB editing/SQL shell.

---

# Final refactor acceptance checklist

Before declaring the refactor finished, verify all items below in a production-like environment.

Verified on 2026-08-28 with a fresh local clone Release build (including deterministic Kiota regeneration and dependency-stamp reuse), 404 passing .NET tests, the complete frontend verification pipeline (188 unit, 72 Chromium, and 72 WebKit tests), standalone `dotnet publish`, ReleaseTool self-tests, and the unified production-image HTTP/failed-Init verifier.

## Repository/build

- [x] frontend history preserved in monorepo
- [x] root solution build succeeds from a clean clone
- [x] frontend dependencies bootstrap only when required
- [x] OpenAPI generation is side-effect free
- [x] Kiota sources regenerate in-place
- [x] CI fails on stale generated client
- [x] full frontend verify still runs separately
- [x] individual .NET project builds remain scoped
- [x] `dotnet publish` produces complete SPA+Backend artifact

## Runtime

- [x] one production MelodyTrack container
- [x] no Node/nginx in final runtime
- [x] Init runs before Backend
- [x] failed Init prevents Backend startup
- [x] Kestrel serves SPA and `/api`
- [x] SPA fallback never catches `/api`, `/health`, `/alive`
- [x] asset caching/compression/security headers verified over HTTP
- [x] production CORS removed
- [x] canonical public base URL used for generated links
- [x] forwarded headers trust only intended proxy/network

## Development

- [x] Aspire AppHost starts Postgres/Init/Backend/Vite/Dashboard
- [x] dev PostgreSQL volume persists
- [x] versioned seed upgrades work
- [x] deterministic dev superuser exists
- [x] dev SQL parameter diagnostics can be enabled without changing production defaults

## API

- [x] no FastEndpoints endpoint remains
- [x] source-generated registration works for every endpoint
- [x] every handler is `public static HandleAsync` with `CancellationToken`
- [x] every operation has stable unique operation ID
- [x] typed results used by default
- [x] native validation preserves existing rules
- [x] centralized Problem Details includes canonical trace ID
- [x] pagination uses `items/page`
- [x] native OpenAPI 3.1 is authoritative
- [x] Scalar/OpenAPI runtime endpoints are Development-only
- [x] FastEndpoints/FastEndpoints.Swagger/FluentValidation removed

## Frontend contract/transport

- [x] Axios removed from API transport
- [x] Kiota Fetch adapter is singleton/application-wide
- [x] generated API models are authoritative
- [x] handwritten duplicate API DTOs removed
- [x] semantic API wrappers remain
- [x] shared refresh operation handles concurrent `401`s
- [x] original request retries at most once
- [x] inactivity/suspend resume is reliable
- [x] temporary network failure does not erase valid session state
- [x] terminal auth failure clears state once
- [x] cancellation/idempotency/blob downloads preserved
- [x] persistent `AppError` UI exposes trace ID where available

## Authentication/security

- [x] JWT uses ES256 only
- [x] issuer/audience/signature/lifetime explicitly validated
- [x] auth secrets are independent
- [x] passwords use Argon2id + password pepper
- [x] portal PIN uses Argon2id + portal PIN pepper
- [x] old sessions revoked at breaking cutover
- [x] first superuser has a documented server-local reset/recovery path
- [x] legacy browser refresh-token migration removed
- [x] CSRF scoped to cookie-authenticated session operations
- [x] coarse role authorization uses DB-backed policies
- [x] `LockedUntil`/dead account-lockout mechanism removed
- [x] rate limits reviewed for brute-forceable anonymous endpoints
- [x] portal PIN cooldown enforced
- [x] portal link remains permanent/reusable and is treated as a credential
- [x] portal normal login supports multiple device sessions
- [x] portal refresh preserves portal sliding lifetime
- [x] no auth/portal credential appears in logs/telemetry

## Data

- [x] Core has no EF dependency
- [x] Data owns EF/migrations/configuration
- [x] versioned AES-256-GCM PII encryption preserved
- [x] PII keys are real high-entropy 256-bit material
- [x] Init migrates/re-encrypts old PII key versions
- [x] missing referenced PII key version fails initialization

## Observability

- [x] Serilog remains logging provider as intended
- [x] SerilogTracing removed
- [x] exactly one intended OTLP log export path
- [x] Backend and Init have distinct service names
- [x] Npgsql traces/metrics enabled
- [x] no production SQL parameter logging
- [x] `X-Trace-Id` equals W3C trace ID
- [x] Problem Details `traceId` equals the same ID
- [x] incoming `traceparent` propagation tested
- [x] backend error -> copied trace ID -> configured telemetry backend search works
- [x] no frontend telemetry or browser OTLP relay is shipped
- [x] telemetry exporter outage does not break app
- [x] production Dashboard operations follow `docs/production-telemetry.md`

## Tests/releases

- [x] integration tests use PostgreSQL Testcontainers
- [x] tests run real Init test mode
- [x] tests use standard ASP.NET Core test host
- [x] unified image integration suite passes
- [x] release tool is monorepo-only
- [x] one-file-per-release changelog works
- [x] release/hotfix version allocation works
- [x] hotfix merge-back to local develop/active release is documented/tested
- [x] merge of valid release/hotfix PR to `master` publishes image/tag/GitHub Release
- [x] deployment remains manual
- [x] obsolete frontend release workflow removed
