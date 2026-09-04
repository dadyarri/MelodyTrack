# MelodyTrack Roadmap

This document is the implementation contract and staged product roadmap for MelodyTrack. Completed stages are retained as the verified architectural/product baseline and are marked with `✅`; detailed implementation history still belongs in Git.

The roadmap is ordered by dependency where a dependency is known. Deliberately deferred stages may be reprioritized when they are independent of the active workstream.

Completed stages are historical contracts: later product stages may explicitly supersede individual decisions from the completed refactor baseline, but do not retroactively rewrite completed-stage scope to make the history look different.

Locked architectural decisions are not invitations for redesign. If implementation exposes a concrete incompatibility, document it and make the smallest change that preserves the intent of this roadmap.

---

# Refactor Baseline

The architecture and execution rules below describe the baseline produced by the completed refactor. Later product stages may intentionally replace specific product-level assumptions, such as the client portal PIN flow.

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

### 1.22 Build contract

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

### 1.23 Publish and image contract

`dotnet publish` for the production Backend application must:

- build the production Vite frontend;
- place the built SPA/static assets into the publish output expected by Kestrel;
- produce a complete runnable application artifact outside Docker.

The Docker build then packages that publish artifact. The final image contains the ASP.NET runtime/application only, not Node or frontend build tooling.

Image integration tests must verify the artifact itself, not an accidental behavior of a separate nginx container.

### 1.24 Testing model

The main backend/integration test suite remains directly runnable with `dotnet test`.

Use:

- xUnit/current test framework;
- PostgreSQL Testcontainers;
- the real `MelodyTrack.Init --mode test` process before Backend startup;
- `WebApplicationFactory<Program>`/standard ASP.NET Core test hosting after FastEndpoints testing infrastructure is removed.

Do not make the main integration suite depend on Aspire AppHost.

Aspire-level distributed tests may be added later only for a concrete cross-resource scenario that cannot be covered more simply.

### 1.25 Release and branch model

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

---

# Refactor Program — Completed

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

Preserve/implement the useful scope from the previously planned session-resume stage:

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

## Explicit non-goals for the refactor

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
- multi-staff-account browser switching before its dedicated product stage;
- offline mutation support before the Offline-First Operations stage;
- Web Push as a prerequisite for any business operation;
- client ability to change teacher/provider in rescheduling;
- recurrence-series changes through client rescheduling;
- god mode user impersonation/arbitrary DB editing/SQL shell.

---

---

## Final refactor acceptance checklist

Before declaring the refactor finished, verify all items below in a production-like environment.

Verified on 2026-08-28 with a fresh local clone Release build (including deterministic Kiota regeneration and dependency-stamp reuse), 404 passing .NET tests, the complete frontend verification pipeline (188 unit, 72 Chromium, and 72 WebKit tests), standalone `dotnet publish`, ReleaseTool self-tests, and the unified production-image HTTP/failed-Init verifier.

### Repository/build

- [x] frontend history preserved in monorepo
- [x] root solution build succeeds from a clean clone
- [x] frontend dependencies bootstrap only when required
- [x] OpenAPI generation is side-effect free
- [x] Kiota sources regenerate in-place
- [x] CI fails on stale generated client
- [x] full frontend verify still runs separately
- [x] individual .NET project builds remain scoped
- [x] `dotnet publish` produces complete SPA+Backend artifact

### Runtime

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

### Development

- [x] Aspire AppHost starts Postgres/Init/Backend/Vite/Dashboard
- [x] dev PostgreSQL volume persists
- [x] versioned seed upgrades work
- [x] deterministic dev superuser exists
- [x] dev SQL parameter diagnostics can be enabled without changing production defaults

### API

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

### Frontend contract/transport

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

### Authentication/security

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

### Data

- [x] Core has no EF dependency
- [x] Data owns EF/migrations/configuration
- [x] versioned AES-256-GCM PII encryption preserved
- [x] PII keys are real high-entropy 256-bit material
- [x] Init migrates/re-encrypts old PII key versions
- [x] missing referenced PII key version fails initialization

### Observability

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

### Tests/releases

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

---

# Post-Refactor Product Roadmap

All stages below are product work on top of the completed refactor baseline. Stages 11–13 are already completed and are retained without changing their implemented scope. Stage numbers are stable identifiers: when scope is merged into another stage, the old number is not reused.


## Stage 11: God Mode and System Notices ✅

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
dedicated HTTPS god mode endpoint
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
- keep the god mode listener off the public application route;
- do not expose it directly from Docker to the Internet;
- direct server access remains the only way to issue a god mode token; network-level restrictions on the dedicated hostname are optional defense-in-depth rather than part of MelodyTrack authentication;
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

Before adding god mode events, centralize the complete audit vocabulary in a backend-owned catalog shared by Backend and Data/Init:

- define every category and event once as a typed definition with stable category/action codes and Russian display labels;
- keep Russian as the only supported UI language and do not introduce culture negotiation, resource files, or other unused localization infrastructure;
- replace free-form category/action strings at audit write sites with catalog definitions, including initialization, security, portal, recurring-task, and other conditionally selected events;
- continue persisting stable codes rather than translated labels so historical records, filtering, exports, and integrations remain durable;
- return both the stable codes and backend-resolved Russian labels from the audit API, with the raw code as a safe fallback for unknown historical records;
- make Russian label searches resolve to the corresponding category/action codes before applying the database query;
- remove the duplicated frontend category/action dictionaries once the generated API contract exposes the labels;
- test catalog uniqueness and label completeness so a new audit event cannot be added without a Russian display label.

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
- god mode is isolated from the public application route and cannot be used without a server-issued one-time token;
- normal superuser credentials cannot authenticate to it;
- one-time god mode tokens cannot be reused;
- credential reset/session revocation actions are enforced server-side;
- users can receive persistent global or targeted notices;
- all current and newly introduced audit categories/events display backend-owned Russian labels, while retaining their stable stored codes;
- all privileged god mode actions are auditable without leaking credentials.

---

## Stage 12: Notification Infrastructure and Web Push ✅

### Goal

Create a reusable notification subsystem for workflow events. In-app notification state is authoritative; Web Push is an optional best-effort delivery channel.

This stage exists primarily to support vacation-approval and appointment-rescheduling requests but should remain general enough for later application workflows.

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

- staff (including superusers) and clients have durable in-app notifications;
- unread/read state works across reloads;
- supported browsers can opt into Web Push;
- unsupported/denied push does not degrade core workflow visibility;
- push contains no unnecessary sensitive details;
- notification creation and push delivery failures are observable.

---

## Stage 13: Vacation and Work-Schedule Requests with Superuser Approval ✅

### Goal

After the notification infrastructure is operational, add auditable approval workflows for vacations requested by teachers, administrators, and clients, plus work-schedule changes requested by teachers and administrators. Superusers are the only role allowed to approve or decline these requests.

An approved request creates the authoritative vacation record or replaces the authoritative weekly work schedule. A pending request must not change calendar availability or be presented as accepted.

### Request sources and authority

- teachers can request vacation only for themselves through the staff application;
- administrators can request vacation only for themselves through the staff application;
- teachers and administrators can request weekly work-schedule changes only for themselves through the staff application;
- clients can request their own vacation through the client portal;
- superusers can review all teacher, administrator, and client vacation requests and all staff work-schedule requests;
- administrators cannot approve vacation or work-schedule requests;
- requesters cannot approve their own request or use another subject identifier;
- any retained superuser direct vacation/work-schedule management is a separate, explicitly audited operation and must not create an approval bypass for other roles.

### Request model and states

Use a first-class vacation request rather than a notification payload. It should record at least:

```text
VacationRequest
- Id
- RequesterPrincipalType/Id
- SubjectType: Staff | Client
- SubjectId
- RequestedStart/End
- Status: Pending | Approved | Declined | Cancelled
- RequestMessage?
- CreatedAtUtc
- ProcessedAtUtc?
- ProcessedBySuperuserId?
- DecisionMessage?
- resulting VacationId?
- concurrency/version field
```

Use the same transition semantics for a first-class work-schedule request. Store an immutable, complete seven-day snapshot so the superuser approves exactly the schedule the requester submitted.

Use the current authoritative vacation range granularity initially. The model and API must have an explicit migration path to the time-aware vacation ranges introduced by the later calendar-workflow stage; once timed vacations exist, requests and approved vacations must use the same timezone/range semantics.

Permit at most one equivalent or overlapping pending request for the same subject where duplicate requests would be ambiguous. Define cancellation rules for a requester withdrawing a still-pending request; processed requests remain immutable history.

### Requester workflows

Staff application:

1. A teacher or administrator selects a vacation range and may add a short message.
2. Backend validates ownership and basic range rules, then creates a `Pending` request.
3. The requester sees the pending request and its later decision in the staff application.
4. A teacher or administrator edits their weekly work schedule in the profile and submits the complete schedule for approval; the current schedule remains active until approval.

Client portal:

1. An authenticated client selects their own vacation range and may add a short message.
2. Backend derives the client identity from the portal session rather than trusting a submitted client ID.
3. The portal shows pending and processed request status even when Web Push is unavailable.

For every request source, submission creates a durable notification for superusers and optional privacy-minimized Web Push. Submission must clearly state that the availability change is awaiting approval.

### Superuser review

Provide a superuser-only review queue with filters for pending/history and enough context to decide safely:

- requester and vacation subject;
- teacher/administrator/client classification;
- requested range and message;
- existing vacations and relevant schedule conflicts;
- request age/status;
- Approve and Decline actions;
- optional short decision message;
- for work-schedule requests, the current and requested seven-day schedules side by side.

Do not expose one client's request or status to another client. Staff requesters can see only their own requests unless separately authorized as a superuser.

### Approval transaction

Approval must be atomic and revalidate current state:

1. caller is still an authorized superuser;
2. request exists and is still `Pending`;
3. subject and requester relationships remain valid;
4. requested range still satisfies vacation, timezone, overlap, and business rules;
5. existing appointments or other conflicts follow an explicit product rule and are never silently deleted, cancelled, or moved;
6. create the authoritative staff/client vacation record;
7. for a work-schedule request, atomically replace the authoritative weekly schedule with the reviewed snapshot instead;
8. mark the request `Approved`, link the created vacation where applicable, and record processor/time;
9. write an audit event and requester notification transactionally or through the established reliable notification boundary.

Concurrent approval/decline attempts must produce one final decision and never create duplicate vacations.

### Decline and cancellation

- only a superuser can decline a pending request;
- decline records the processor, time, and optional message without creating a vacation;
- a requester may cancel only their own pending request;
- approval, decline, and cancellation create durable status history and the appropriate requester notification;
- notification or push-delivery failure must not roll back the decision.

### Authorization and abuse controls

- enforce the role/ownership matrix server-side, not only by hiding controls;
- client-portal requests use the portal's client identity and cannot target staff or another client;
- teachers/administrators cannot submit on behalf of another user;
- all decisions and direct superuser vacation changes are audited without logging private messages or unnecessary PII;
- apply idempotency/concurrency protection and bounded request-rate controls;
- reject malformed, inverted, empty, or otherwise invalid ranges consistently through Problem Details.

### Testing

Cover at least:

- teacher and administrator self-request happy paths;
- teacher and administrator work-schedule self-request happy paths;
- pending work-schedule requests leaving current availability unchanged;
- work-schedule approval applying the reviewed seven-day snapshot exactly once;
- client-portal self-request and cross-client denial;
- superuser-only queue, approval, and decline;
- administrator/teacher/requester approval denial;
- pending request cancellation;
- duplicate and overlapping pending requests;
- range, timezone, existing-vacation, and appointment-conflict validation;
- concurrent approve/decline and retry behavior;
- approved vacation creation exactly once;
- notifications for superusers and requesters, including push failure;
- audit events and privacy-safe API/browser behavior;
- later migration to timed vacation ranges.

### Done looks like

- teachers and administrators can request their own vacations and work-schedule changes but cannot activate them without superuser approval;
- clients can submit and track their own vacation requests through the client portal;
- superusers have one clear queue for staff and client vacation decisions;
- approval creates exactly one authoritative vacation or applies exactly one reviewed weekly schedule after revalidation;
- pending/declined/cancelled requests never affect availability;
- every transition is authorized, auditable, concurrency-safe, and durably communicated through the notification system.

---

## Stage 14: Calendar Workflow and Income Forecast Improvements

### Goal

Address the latest customer feedback around trial lessons, calendar workflow, schedule visibility, and forward-looking income.

### Remaining scope

- render trial lessons on the calendar with a distinct color that is not reused by ordinary appointment states;
- add the missing recurring-task reminder for trial lessons;
- allow vacations to include a start and end time instead of being date-only;
- upgrade the Stage 13 vacation-request forms, validation, superuser review, and approved-vacation creation to use the same time-aware range;
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
- integration and browser tests cover role visibility, calendar gestures, trial-lesson presentation/reminders, timed vacation requests/approvals and boundaries, and forecast calculations.

---

> **Course-progress scope consolidation:** the former standalone Stage 15, **Services Progress**, is no longer an independent implementation stage. Its remaining scope is consolidated into Stage 24, **Course Progress Integration and Hardening**, so course progress is redesigned together with the client portal instead of being restored twice. The Stage 15 number is not reused.

---

## Stage 16: Multiple Staff Accounts in One Browser — Planned for later

Allow the main staff portal to remember several staff user accounts in one browser and switch between them without repeatedly entering full credentials. This stage intentionally changes the refactor-era “one renewable identity per browser profile” limitation for **staff accounts only**.

Client-portal identities remain a separate experience and are not part of the staff account switcher.

### Remaining scope

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

## Stage 17: Offline-First Operations Architecture — Planned for later

Design how MelodyTrack can keep the most common daily staff work available during internet outages and infrastructure shutdowns. The target is more than cached read-only pages: authorized staff should be able to create, edit, and delete supported records locally, close the browser if necessary, and synchronize safely when Backend returns.

This is an architecture/product-discovery stage. It must produce a validated design and thin end-to-end prototype before broad implementation. Do not enable offline mutations across all domains until conflict, security, and reconciliation rules are explicit.

### Remaining scope

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
- Design sync coordinator around login, Stage 16 account switching, connectivity changes, app startup, browser background limitations, and manual retry.
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

## Stage 18: Accounting and Staff Compensation Architecture — Planned for later

Define how accounting should integrate with MelodyTrack's existing services, appointments, payments, expenses, users, and statistics. The design must cover staff salary calculation/payment while keeping scheduling, cash movement, earned revenue, expenses, payroll liabilities, and actual payouts as distinct concepts.

This is an accounting-domain discovery/architecture stage. Validate the model with the people who will reconcile the numbers before building UI or automating salary calculations.

### Remaining scope

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

---

### Done looks like

- revenue, cash receipts, receivables, expenses, salary accruals, liabilities, payouts, and profit each have one documented meaning/reconciliation rule;
- representative fixed/hourly/per-appointment/percentage/bonus/deduction/substitution/cancellation/refund/mid-period-rate-change scenarios produce explainable expected results;
- approved periods preserve historical calculations and corrections remain traceable through adjustments/reversals;
- permissions protect salary data and prevent one user from silently changing agreements, approving payroll, and erasing resulting audit history;
- boundary between MelodyTrack accounting and external statutory responsibilities is explicit;
- a reviewed incremental implementation plan can add useful accounting capabilities without breaking current payment/expense/statistics behavior.

---

---

# Client Portal Redesign Program

The portal redesign is a dedicated product workstream starting at Stage 20. Stage 19 is intentionally left unassigned so the redesign keeps its agreed Stage 20–39 numbering. The program intentionally supersedes the refactor-era client portal PIN model while preserving the completed refactor history above.

Stages 20–38 form the main redesign sequence. Stage 39 is an optional follow-up optimization and is not required to complete the redesign.

## Stage 20: Portal Architecture and Access Foundation

The client portal should become a distinct application surface with its own navigation, session behavior, visual system, and client-specific components. The current PIN-based access flow should be replaced with permanent passwordless portal links.

### Remaining scope

- Treat the client portal as a separate application surface from the administrative UI.
- Establish the new portal shell, routing boundaries, client-specific navigation, and session model.
- Replace the current portal PIN flow with permanent opaque portal links.
- Generate a random URL-safe portal token with approximately 128 bits of entropy.
- Store only a hash of the portal token on the existing user entity.
- Enforce uniqueness of the stored token hash.
- Resolve `GET /p/{token}` by hashing the supplied token, finding the corresponding user, resolving or creating the current browser/device session, activating the client profile, and redirecting to the normal portal home route.
- Allow one browser/device session to remember multiple authorized client users.
- Treat portal-link activation as idempotent for a client already remembered in the current browser/device session:

  - do not create a duplicate remembered-profile/session membership;
  - make that client the active profile;
  - redirect directly to the portal home page without showing an intermediate profile chooser.
- Opening a different client's valid portal link in the same browser should add that client to the existing browser/device session and make that client active.
- Enforce uniqueness of the browser-session/client membership at the database level, conceptually `UNIQUE(DeviceSessionId, UserId)`.
- Provide client-profile switching and removal for remembered clients without introducing PIN confirmation.
- Do not keep the portal token in the URL during ordinary portal navigation.
- Add an administrative action for regenerating a client's portal link.
- Regenerating a link must immediately invalidate the previous token.
- Remove the existing PIN mechanism completely.

### Deferred product work

- Player aliases are handled in Stage 21.
- Multi-profile session behavior is established in this stage; the final profile-switcher placement and presentation are integrated into the portal shell in Stage 23.
- RPG visuals are handled in Stage 22.
- Course progress, structured homework, trainers, scoring, and leaderboards are handled separately.

### Integration with the refactored architecture

- Keep portal access tied to the existing user model rather than introducing a separate client identity table.
- Portal authentication should resolve users through the token hash and then rely on secure browser/device session state.
- Use secure `HttpOnly` session cookies after the initial portal-link access.
- Portal link regeneration must use the same administrative authorization model as other client-management operations.
- Errors should use the existing Problem Details/AppError/trace-ID flow.

### Done looks like

- A client can open a permanent personal portal link without a username, password, or PIN.
- The same link remains usable until explicitly regenerated.
- The raw token is not stored in the database.
- Regenerating a link invalidates the old one immediately.
- Normal portal navigation no longer exposes the token in the address bar.
- Reopening the same personal portal link in the same browser does not create duplicate session/profile membership and goes directly to that client's portal home.
- Opening another client's portal link adds that client to the same browser/device session, and remembered clients can be switched without a PIN.

---

## Stage 21: Player Identity and Alias Dictionaries

Each portal client should have a stable public fantasy identity used in the portal header and competitive features. Alias generation should be server-controlled and backed by administrator-managed Russian dictionaries.

### Remaining scope

- Add nullable `PortalAlias` to the existing user entity.
- Enforce database-level uniqueness for non-null aliases.
- Keep the client's real or administrative name unchanged outside the public portal identity.
- Support exactly two alias patterns:

  - positive fantasy/magical epithet + fantasy creature;
  - positive fantasy/magical epithet + musical/bardic term.
- Add three database-backed dictionaries:

  - `AliasEpithets`;
  - `AliasCreatures`;
  - `AliasBardicTerms`.
- Each dictionary entry should contain only:

  - `Id`;
  - `Value`;
  - `IsEnabled`.
- Do not add:

  - manual weight fields;
  - category/type fields;
  - created/updated timestamps.
- Prevent duplicate values inside each dictionary.
- Add a database migration that creates all three dictionaries and seeds an initial usable Russian-language vocabulary for:

  - positive fantasy/magical epithets;
  - fantasy creatures;
  - musical/bardic terms.
- Treat seeded values as normal editable dictionary entries after migration.
- Add administrative CRUD for all three dictionaries:

  - list;
  - search;
  - add;
  - edit;
  - enable/disable;
  - delete.
- Dictionary changes must affect newly generated candidates without requiring application deployment.
- If `PortalAlias == null`, require alias selection on first portal entry.
- Return one generated candidate from the server.
- Provide `Another name` and `Choose this name` actions.
- Each `Another name` request should return exactly one new candidate.
- Do not send the complete dictionaries to the frontend.
- Generate candidates randomly from enabled dictionary values.
- Store the selected alias as a final string on the user.
- Existing aliases must not change when dictionary values are later edited or deleted.
- Avoid returning already assigned aliases where possible.
- Keep the database unique constraint as the final protection against concurrent alias collisions.
- If a chosen alias loses a save race and the database reports that it has become unavailable:

  - do not generate or assign a replacement automatically;
  - return a visible conflict error;
  - keep the client on the alias-selection screen;
  - let the client explicitly press `Another name` to request a new candidate.

### Deferred product work

- Dynamic anti-repetition weighting is deferred to Stage 39.
- No manual per-word weighting is required.
- Avatars, titles, cosmetics, frames, and similar profile customization are not part of this stage.
- Leaderboards are handled later.

### Integration with the refactored architecture

- Keep aliases directly on the existing user entity rather than introducing a separate portal profile.
- Keep alias dictionaries in the database because administrators must manage them through MelodyTrack.
- The generator may cache enabled dictionary values in memory, but administrative mutations must invalidate the relevant cache.
- Existing assigned aliases remain independent from current dictionary contents.

### Done looks like

- A new portal client must choose a unique fantasy alias before normal portal use.
- Initial Russian dictionaries are available immediately after migration.
- Administrators can manage all three dictionaries without deployment.
- Existing aliases survive later dictionary edits and deletions.
- Alias uniqueness is enforced even under concurrent selection.
- A client who loses an alias-selection race sees an explicit conflict and remains in control of generating the next candidate.

---

## Stage 22: RPG Design System

The redesigned client portal should look and feel like an RPG interface rather than a conventional SaaS application. The visual system should be established before the remaining portal pages are rebuilt.

### Remaining scope

- Define a dedicated fantasy/RPG visual language for the client portal.
- Create portal-specific reusable UI components for:

  - navigation;
  - portal header;
  - player identity;
  - course presentation;
  - homework presentation;
  - trainer presentation;
  - leaderboard presentation;
  - progress indicators;
  - dialogs;
  - buttons;
  - reusable panels/cards.
- Use fantasy and magical visual motifs.
- Support game-like panels and controls.
- Add expressive animated UI states and magical visual effects where appropriate.
- Keep animations consistent across components instead of implementing unrelated effects page by page.
- Support reduced-motion behavior.
- Keep the client portal visually independent from the administrative Ant Design language.

### Deferred product work

- Individual portal pages are implemented in later stages.
- Final animation polish is deferred to Stage 37.
- No additional gamification mechanics are introduced here.

### Integration with the refactored architecture

- The administrative UI may continue using Ant Design.
- The client portal should use a dedicated component layer and theme.
- Shared low-level utilities may still be reused where they do not leak administrative styling into the portal.
- Portal components should be reusable across home, courses, homework, trainers, and leaderboards.

### Done looks like

- Later portal features can be implemented against a coherent RPG-style component system.
- The portal no longer visually reads as Ant Design with fantasy decoration.
- Reduced-motion users retain a usable interface.

---

## Stage 23: New Portal Shell and Home

The new portal shell should establish the redesigned user experience before the more specialized course, homework, trainer, and leaderboard features are introduced.

### Remaining scope

- Build the new portal shell using the new access model, player aliases, and RPG design system.
- Establish the main portal areas:

  - Home;
  - Courses;
  - Homework;
  - Trainers;
  - Leaderboards;
  - profile switching.
- Display `PortalAlias` prominently in the portal header.
- Build the new RPG-style home page.
- At this stage, the home page may use existing course and homework data where the replacement systems are not yet available.
- Provide navigation to:

  - course content;
  - homework;
  - standalone trainers;
  - leaderboards;
  - profile switching when multiple clients are saved in the browser.

### Deferred product work

- Course progress is integrated in Stage 24.
- Structured homework is implemented in Stage 25.
- Trainers and leaderboards are implemented later.
- Final gamification data on the home page is added in Stage 35.

### Integration with the refactored architecture

- Keep the portal shell independent from the administrative shell.
- Route all later client-facing features through the new portal navigation.
- Reuse the RPG component system from Stage 22.
- Preserve existing authorization rules while moving portal presentation into the new shell.

### Done looks like

- The redesigned portal is usable before all later features are complete.
- Clients see the new RPG shell, navigation, alias, and home page.
- New portal functionality can be added without rebuilding the shell again.

---

## Stage 24: Course Progress Integration and Hardening

MelodyTrack should restore client-facing course progress as part of the redesigned portal while hardening the existing course-progress domain for daily teaching use. Services remain appointment and billing concepts; courses represent a client's long-term learning path.

### Remaining scope

- Reconsider and approve the client-facing course-progress experience before restoring it to the portal.
- Use `docs/course-progress-map-brief.md` as design context, not as an implementation checklist.
- Integrate the approved course-progress experience into the new RPG portal rather than restoring the previous UI unchanged.
- Keep the client schedule independently accessible from course progress.
- Harden:

  - course enrollment;
  - dependency behavior;
  - unlocking;
  - completion;
  - points;
  - auditing;
  - course-template evolution.
- Add focused backend and frontend verification for:

  - course assignment;
  - appointment-to-theme linkage;
  - progress transitions;
  - authorization.
- Improve explanations and recovery paths for:

  - blocked themes;
  - invalid dependency graphs;
  - stale enrollment state.
- Keep completion teacher-controlled.
- Do not infer completion from:

  - appointment count;
  - notes;
  - homework submission alone;
  - trainer usage.

### Deferred product work

- Chat is outside this stage.
- Homework uploads are outside this stage.
- Automated homework checking is outside this stage.
- Feedback threads are outside this stage.
- Achievements are outside this stage.
- Leaderboards are outside this stage.
- Marketplace features are outside this stage.
- Inline media attachments are outside this stage.
- Shared theme-content access remains deferred until its client-facing visual and usability direction is approved.
- Structured homework and trainers are implemented in later stages.

### Integration with the refactored architecture

- New or changed APIs use generated Minimal API registration and Kiota DTOs.
- Authorization uses the centralized policy model plus resource ownership checks.
- Errors use Problem Details/AppError/trace-ID UI.
- Preserve auditability and OpenTelemetry visibility for important failures.
- Notifications may be used when they provide a real workflow benefit, but ordinary progress-state changes should not become push noise.
- Client-facing progress must follow the new RPG design system.

### Done looks like

- Staff can run course enrollment and progress without fragile manual workarounds.
- Progress rules and point changes are explicit, auditable, and covered by focused tests.
- Clients can access only their own approved portal surfaces.
- The restored client course-progress UI is understandable and useful in normal teaching work.

---

## Stage 25: Replace BBCode Homework with Structured Documents

Homework should move from SCEditor/BBCode to a structured document model capable of supporting both rich text and interactive application nodes. Existing BBCode data does not need to be migrated.

### Remaining scope

- Replace BBCode homework storage with versioned JSON documents.
- Use a document envelope containing:

  - `schemaVersion`;
  - the structured document payload.
- Replace SCEditor with Tiptap/ProseMirror.
- Restore the formatting features already available in the current homework editor:

  - paragraphs;
  - bold;
  - italic;
  - underline;
  - strikethrough;
  - links;
  - lists;
  - blockquotes;
  - code blocks.
- Use one shared document schema for:

  - editing;
  - persistence;
  - read-only rendering.
- Validate:

  - document schema version;
  - allowed nodes;
  - document size;
  - references contained in custom application nodes.
- Remove:

  - SCEditor;
  - `BbcodeEditor`;
  - `BbcodeContent`;
  - BBCode parsing;
  - BBCode-specific homework contracts.

### Deferred product work

- Custom application nodes are added in Stage 26.
- Interactive trainers are added later.
- No BBCode migration layer is required.

### Integration with the refactored architecture

- Avoid recreating the current split between one editor implementation and a separate custom display parser.
- Keep the structured document format compatible with Tiptap/ProseMirror.
- Do not create a large C# hierarchy mirroring every ProseMirror node unless server-side behavior later requires it.
- Use the existing backend error and authorization conventions.

### Done looks like

- Homework is no longer stored or rendered as BBCode.
- Editing and read-only display use the same document schema.
- The document model can safely host future interactive nodes.

---

## Stage 26: Custom Homework Document Nodes

Homework documents should support real MelodyTrack components rather than only formatted text.

### Remaining scope

- Add reusable custom-node infrastructure to the structured homework model.
- Implement the first custom node:

  - `practiceExercise`.
- Store only an exercise reference in the homework document rather than embedding the full exercise configuration.
- Render custom nodes through React NodeViews in the editor.
- Show teachers a meaningful exercise preview rather than raw JSON or an opaque ID.
- Support editing and removing the embedded exercise node.
- Render the same node as the interactive exercise runtime in the client portal.
- Keep the node infrastructure general enough for future application-specific nodes.

### Deferred product work

Possible future node categories may include:

- audio;
- attachment;
- quiz.

They do not need to be implemented in this stage.

### Integration with the refactored architecture

- Keep the document node as a reference to the exercise domain.
- Do not couple the homework editor to trainer runtime internals.
- Reuse the same structured document schema for both teacher and client rendering.

### Done looks like

- Teachers can insert application-specific exercise nodes into homework.
- Clients see those nodes as real interactive components.
- The custom-node mechanism is reusable beyond the first trainer.

---

## Stage 27: Shared Practice Platform Architecture

Interactive practice should be implemented as one reusable platform shared by homework, standalone trainer pages, the client portal, and the primary authenticated MelodyTrack application.

Trainer implementations must not depend on a particular UI surface.

### Remaining scope

- Keep exercise type separate from exercise configuration.
- Use a reusable model conceptually similar to:

```text
PracticeExercise
├── Id
├── Type
└── Configuration
```

- Keep trainer-specific configuration structured and versionable.
- Do not create a separate exercise type for every parameter combination.
- Separate:

  - exercise definition;
  - configuration;
  - runtime;
  - timing/audio engine;
  - rendering;
  - optional browser audio-analysis integration.
- Ensure the same trainer runtime can be used:

  - inside a homework document;
  - in teacher preview;
  - from standalone trainer pages in the client portal;
  - from standalone trainer pages in the primary authenticated MelodyTrack application.
- Do not create separate trainer implementations for staff/main-application and client-portal use.
- All supported exercise parameters must be explicitly configurable by the teacher when preparing an exercise. Do not hide required teaching parameters behind fixed hard-coded presets.

### Shared rhythmic values

Use one common rhythmic-value model across trainers.

The currently required values are:

- whole note;
- half note;
- quarter note;
- eighth note;
- sixteenth note;
- eighth-note triplet.

Internally, timing must be represented independently from rendered notation so the same musical structure drives:

- visual notation;
- simplified trainer visualization;
- metronome synchronization;
- playhead movement;
- browser audio-analysis expectations.

### Shared tempo behavior

Trainer configurations that use tempo should support:

- BPM;
- time signature;
- count-in where applicable;
- looping where applicable;
- optional automatic BPM progression.

Automatic BPM progression must support configuration of:

- BPM increment;
- trigger interval;
- trigger unit:

  - after N bars;
  - or after N completed repetitions.

### Runtime timing

Use the Web Audio API clock as the timing authority.

Schedule musical events against:

```text
AudioContext.currentTime
```

React renders current state and the visual playhead but does not act as the musical clock.

Do not use `setInterval()` as the timing authority.

### Deferred product work

- Browser audio analysis and local recording are implemented separately in Stage 32.
- Scale practice remains a future trainer idea.
- Picking/arpeggio training is not part of the currently approved trainer set.
- Exercises requiring ML-based microphone evaluation are outside the current trainer platform scope.

### Integration with the refactored architecture

- Keep the trainer runtime independent from the Tiptap homework editor.
- Homework documents reference exercises rather than embedding trainer runtime implementation details.
- Keep trainer configuration compatible with both embedded and standalone execution.
- Reuse the same timing primitives across all trainer types.

### Done looks like

- MelodyTrack has one trainer architecture shared by every application surface.
- Trainer configuration, timing, rendering, and optional analysis concerns are separable.
- Homework and standalone training do not require duplicate trainer implementations.
- New trainer types can reuse the platform without changing homework infrastructure.

---

## Stage 28: Guitar Strumming Trainer

The first trainer should implement configurable guitar-strumming exercises while explicitly separating the rhythmic action performed on the strings from the physical hand-motion pattern.

The trainer may optionally include the chord-change runtime from Stage 29 so one exercise can train strumming and chord changes together.

### Remaining scope

Support configuration for:

- BPM;
- time signature;
- number of bars;
- number of repetitions;
- the shared supported rhythmic values;
- rests in the rhythmic pattern;
- accents;
- count-in;
- looping;
- automatic BPM progression.

### Structured strumming model

Do not store the exercise as a compact pattern string such as:

```text
D-DU-UDU
```

Represent the rhythmic pattern structurally.

Each position on the musical timeline must distinguish:

1. hand movement;
2. action performed on the strings;
3. accent state.

Conceptually:

```text
StrummingEvent
├── RhythmicValue
├── HandMotion
├── Action
└── Accent
```

### Hand movement

Support exactly the currently required hand-motion directions:

```text
HandMotion
├── Down
└── Up
```

Hand movement is an independent exercise layer.

Do not infer hand movement from the action on the strings. The configured hand-motion direction may differ from the visible rhythmic-stroke direction.

A rest in the rhythmic pattern does not imply that hand movement stops.

### Actions on the strings

Support the following explicit actions:

```text
StrumAction
├── Down
├── Up
├── DownClick
├── DownDeadNotes
├── UpDeadNotes
├── DownBassStrings
├── UpBassStrings
├── DownTrebleStrings
├── UpTrebleStrings
└── Rest
```

Their meanings are:

- normal downstroke;
- normal upstroke;
- downstroke with click;
- downstroke on dead notes;
- upstroke on dead notes;
- downstroke across the three thick/bass strings;
- upstroke across the three thick/bass strings;
- downstroke across the three thin/treble strings;
- upstroke across the three thin/treble strings;
- rest/no audible stroke.

Do not model these as combinations inferred from several loosely related flags when the configured action itself has a distinct teaching meaning.

### Accents

Any sounding strum action may be marked as accented.

Accent is a property of the action, not a separate hand motion and not a separate rhythmic event.

The exercise UI must make the required accent visually explicit without changing the hand-motion layer.

### Visual presentation and layout

Use the provided strumming-trainer reference as a layout reference, not as a visual-style reference.

Keep the MelodyTrack RPG/fantasy visual system.

Arrange the trainer in this order:

1. **Rhythmic pattern**
   - large visual action cells;
   - synchronized musical notation for the same structured pattern;
2. **Hand movement**
   - a separate synchronized row showing the configured `Down`/`Up` trajectory;
3. **Optional chord block**
   - uses the shared chord-change runtime from Stage 29;
   - can be enabled or disabled for the exercise;
4. **Shared exercise controls**
   - BPM;
   - time signature;
   - rhythmic settings;
   - bar/repetition settings;
   - count-in;
   - loop;
   - metronome;
   - automatic BPM progression;
   - start/stop and related runtime controls.

The arrow/grid representation and musical notation must be two views of the same structured exercise data rather than independently maintained patterns.

### Optional chord-change layer

Allow the teacher to enable the chord-change trainer inside a strumming exercise.

When enabled, the exercise runs three synchronized layers on the same musical timeline:

```text
rhythmic actions
hand movement
chord progression
```

Do not run separate clocks for strumming and chord changes.

The embedded chord layer reuses all relevant Stage 29 behavior, including fixed/random progression, chord durations, next-chord preview, and optional fingering visualization.

The same chord-change runtime must remain usable independently as its own trainer.

### Playback

Use the shared Web Audio timing engine.

React should render:

- current rhythmic position;
- current hand movement;
- current expected action;
- current chord when the chord layer is enabled;
- playhead;
- current repetition/bar;
- current BPM.

### Audio-analysis boundary

The base trainer does not attempt to determine from microphone audio:

- hand movement where no sound was produced;
- physical hand direction;
- reliable downstroke/upstroke classification;
- which part of the strings the hand physically crossed.

Browser audio analysis in Stage 32 evaluates audible events and timing without pretending to reconstruct the complete hand-motion layer.

### Integration with the refactored architecture

- Implement through the shared practice runtime.
- Keep both visual representations derived from the structured exercise configuration.
- Support:

  - homework embeds;
  - teacher preview;
  - standalone use in the primary authenticated application;
  - standalone use in the client portal.

### Done looks like

- Hand movement and actions on the strings are modeled independently.
- Every approved string action, accent, and rest can be represented explicitly.
- Arrow/grid and musical-notation views remain synchronized.
- The optional chord layer can train strumming and chord changes in one exercise without duplicating runtime logic.
- The trainer runs from all intended MelodyTrack surfaces.

---

## Stage 29: Chord-Change and Rhythm Trainers

After the strumming runtime validates the shared timing model, implement the two additional trainer types currently approved for the product.

The chord-change runtime must work both independently and as the optional chord layer inside the Guitar Strumming Trainer.

### Chord-change trainer

The chord-change trainer should display and time transitions between teacher-configured chords under the shared metronome.

Support:

- chord list;
- fixed-order mode;
- random-next-chord mode;
- BPM;
- time signature;
- count-in;
- repetitions or exercise duration;
- looping where applicable;
- automatic BPM progression;
- optional chord-fingering visualization.

#### Fixed-order mode

In a fixed sequence, each chord event may have its own duration.

Default duration:

```text
1 bar
```

The teacher may manually change the duration of each chord with a resolution of one sixteenth note.

Conceptually:

```text
ChordEvent
├── Chord
└── Duration
```

The timeline must therefore support durations such as one bar, half a bar, three quarters of a bar, or another duration representable as an integer multiple of a sixteenth note.

#### Random mode

In random mode, the teacher configures the allowed chord set and one shared duration used by every randomly selected chord.

The next randomly selected chord must never equal the currently active chord.

Given:

```text
F, C, Am
```

this is valid:

```text
F → C → Am → C → F
```

and this is invalid:

```text
F → F → Am → C
```

A chord may appear again later in the sequence.

Conceptually:

```text
candidates = configuredChords excluding currentChord
nextChord = random(candidates)
```

Random mode requires at least two distinct configured chords.

Do not add random ordering to the other currently approved trainer types.

#### Current and next chord

In standalone presentation, show only:

- the current chord;
- the next chord.

Do not show a longer queue of future chords.

The next chord must be selected/resolved and displayed exactly one full bar before the transition so the student has time to prepare.

This rule applies to both fixed and random modes.

#### Chord fingering visualization

Show a chord-fingering diagram for the current/next chord when fingering hints are enabled.

Provide an exercise/display parameter that hides fingering diagrams while keeping chord names visible so the student can progressively practise from memory.

The diagram orientation must match the approved reference:

- the nut is on the left;
- frets progress from left to right;
- strings are horizontal;
- from top to bottom the strings are ordered from the 1st/high-E thin string to the 6th/low-E thick string;
- open-string and muted-string markers appear to the left of the nut;
- finger positions appear as numbered circles on the corresponding string/fret positions.

Do not rotate the diagram into the more common vertical-neck orientation.

### Rhythm trainer

Implement a trainer for practising rhythm independently from a particular chord progression or strumming technique.

Support:

- BPM;
- time signature;
- number of bars;
- count-in;
- looping;
- automatic BPM progression;
- rhythmic event sequence;
- whole notes;
- half notes;
- quarter notes;
- eighth notes;
- sixteenth notes;
- eighth-note triplets;
- rests;
- accents;
- ties.

#### Rhythmic events and rests

Represent the pattern structurally rather than as display text.

Conceptually:

```text
RhythmEvent
├── RhythmicValue
├── IsRest
├── Accent
├── TieFromPrevious
└── TieToNext
```

Rests occupy real musical duration and participate in bar-length validation.

Accent may be set on any sounding rhythmic event. A rest cannot be accented.

All supported rhythmic values may be mixed within the same bar as long as the bar remains musically valid.

#### Eighth-note triplets

Eighth-note triplets are inserted and edited only as a complete group of three.

Conceptually:

```text
EighthTripletGroup
├── Event 1
├── Event 2
└── Event 3
```

Do not allow isolated eighth-triplet members to be inserted independently outside a complete triplet group.

#### Bar validation

Incomplete bars and pickup/anacrusis bars are not supported.

Every configured bar must be completely and correctly filled according to the configured time signature before the pattern is considered valid.

The editor should make invalid/unfinished bar duration explicit and prevent such a pattern from being treated as a valid completed exercise configuration.

#### Ties

Support ties between adjacent sounding notes:

- within a beat;
- across beat boundaries;
- across bar boundaries.

Tied segments represent one continuing sound rather than repeated attacks.

The data model may represent ties through adjacent event references/flags, but the runtime semantics are fixed:

- the first segment begins the sound;
- a tied continuation extends it;
- no new onset is expected at the beginning of a tied continuation.

Stage 32 audio analysis must use the same semantics and must not classify the absence of a new attack on a tied continuation as a missed note.

#### Visual presentation

Render the same structured rhythm in:

- standard musical notation;
- a simplified synchronized timeline/grid with the current playhead.

Both views must be generated from one underlying rhythmic structure.

### Deferred trainer ideas

Do not implement a picking/arpeggio trainer.

Keep a scale trainer as an idea only.

If scale training is revisited later, its visual model must support:

- note names / musical notes;
- tablature;
- fingering.

The exact scale-training interaction model remains intentionally undefined.

### Integration with the refactored architecture

Both approved trainers reuse:

- `PracticeExercise`;
- common rhythmic values;
- the shared timing engine;
- shared metronome behavior;
- homework custom-node integration;
- primary-application standalone execution;
- client-portal standalone execution.

### Done looks like

- Chord-change exercises support fixed progressions with per-chord durations and non-repeating random transitions with one shared random-mode duration.
- The current and next chord are shown with a one-bar preview window.
- Chord fingering can be shown or hidden without hiding the chord name.
- Rhythm exercises support every currently required rhythmic value, rests, accents, complete triplet groups, and ties.
- Rhythm bars are always complete and valid; pickup bars are not accepted.
- Neither trainer introduces a separate timing/runtime subsystem.

---

## Stage 30: Shared Metronome and Exercise Timing

Every currently approved trainer operates under the shared metronome.

The metronome should be a reusable timing component rather than a separate implementation inside each trainer.

### Remaining scope

Support:

- enabled/disabled state;
- BPM;
- volume;
- time signature;
- accented first beat where applicable;
- count-in;
- manual tempo;
- automatic exercise-following mode;
- automatic BPM progression defined by the active exercise.

### Homework behavior

A homework page may contain several exercises.

By default, the page-level metronome follows the currently active exercise.

For example:

```text
Exercise A → 70 BPM
Metronome  → 70 BPM

Exercise B → 95 BPM
Metronome  → 95 BPM
```

Exercises publish their desired timing configuration to the shared metronome rather than creating separate global metronomes.

### Standalone behavior

Use the same metronome/timing implementation when a trainer runs:

- in the client portal;
- in the primary authenticated MelodyTrack application.

Standalone trainers should therefore behave musically the same way as their homework-embedded versions.

### Manual override

Allow the user to override automatically followed tempo.

The UI must clearly indicate when automatic exercise tempo following is disabled by a manual override.

### Automatic BPM progression

Implement the shared progression behavior defined in Stage 27.

After the configured number of:

- bars;
- or completed repetitions,

increase BPM by the configured amount.

The exercise configuration determines whether this behavior is enabled.

### Integration with the refactored architecture

- Keep one timing authority based on Web Audio rather than React timers.
- Keep metronome state at page/service/runtime scope rather than duplicating musical clocks inside individual trainer components.
- Ensure several embedded exercises can safely share the page-level metronome.

### Done looks like

- Every trainer uses one common musical clock and metronome implementation.
- Switching active homework exercises switches metronome configuration correctly.
- Standalone and embedded trainer timing is consistent.
- Automatic BPM progression works by bars or repetitions.

---

## Stage 31: Shared Trainer Catalogue and Cross-Surface Availability

Trainers should be usable independently from homework and courses and must not be restricted to the client portal.

### Remaining scope

Expose standalone trainers in both:

- the client portal;
- the primary authenticated MelodyTrack application.

Both surfaces use the same trainer definitions, configurations, and runtime implementations.

Available standalone trainers at this point are:

- guitar strumming;
- chord changes;
- rhythm.

Appropriate users of either surface should be able to:

- browse trainer types;
- open a trainer;
- configure its supported parameters;
- start/stop practice;
- use it independently from a course;
- use it independently from homework.

Homework exercises continue to reuse the same runtimes.

### Configuration

Do not create simplified hidden trainer implementations for standalone use.

Expose the same underlying configuration capabilities supported by each trainer.

All exercise parameters required for teaching must remain manually configurable rather than being replaced by hard-coded presets.

### Deferred product work

The architecture may later support:

- saved presets;
- recent configurations;
- favorites;
- teacher presets;
- practice history.

These remain future work unless separately scheduled.

### Integration with the refactored architecture

- Reuse the same trainer entity/configuration/runtime on all surfaces.
- Do not create separate client and primary-application trainer implementations.
- Client-facing presentation follows the RPG portal design system.
- Primary-application presentation follows the main application UI while preserving identical trainer behavior and musical semantics.

### Done looks like

- The same trainer can run in homework, the client portal, and the primary MelodyTrack application.
- Standalone practice does not require course enrollment or homework.
- Trainer behavior does not diverge between application surfaces.

---

## Stage 32: Browser Audio Analysis and Local Practice Recording

Add optional microphone-based analysis to supported trainer sessions using browser-native audio capabilities.

This stage intentionally avoids machine-learning models and server-side audio processing.

Raw recordings must remain on the user's device and must not be uploaded to MelodyTrack.

### Audio capture

Request microphone access only when the user explicitly starts recording or enables analysis.

Use:

```text
navigator.mediaDevices.getUserMedia(...)
```

to obtain the microphone `MediaStream`.

Feed the same stream into separate recording and analysis consumers:

```text
Microphone MediaStream
        ├── MediaRecorder → local recording
        │
        └── Web Audio API
                ↓
           AudioWorklet
                ↓
          non-ML DSP analysis
```

Do not send the raw microphone stream to Backend.

### Real-time processing

Use `AudioWorklet` for realtime PCM processing outside the React UI thread.

Keep React responsible for displaying analysis results rather than processing realtime audio blocks.

Start with TypeScript/JavaScript DSP inside the worklet where it is sufficient.

WebAssembly may be used only as an internal DSP implementation detail if profiling or a selected DSP library justifies it.

Do not migrate the React application or the frontend as a whole to WebAssembly.

### Non-ML onset and timing analysis

Implement detection of audible attacks/onsets using conventional signal-processing techniques such as:

- amplitude/energy envelope changes;
- spectral-flux style onset detection;
- adaptive thresholds;
- minimum spacing/debouncing between detected attacks.

Compare detected attacks against the expected exercise timeline generated by the same Web Audio timing system.

For expected audible events, support classifications such as:

- on time;
- early;
- late;
- missed;
- unexpected/extra attack.

Calculate useful session-level timing information such as:

- average timing offset;
- timing variance/consistency;
- tendency to speed up or slow down.

Include a latency/tolerance mechanism so constant device/input latency is not incorrectly presented as a musical timing error.

### Rhythm and tie semantics

The rhythm trainer's structured timeline is authoritative for expected onsets.

For tied notes:

- expect an onset only on the first segment;
- do not expect a new onset on tied continuation segments;
- do not report the intended continuation as a missed attack.

### Accent analysis

Where real-device testing shows useful results, estimate expected accents using relative local signal energy around detected onsets.

Treat this as approximate feedback rather than an exact measurement of technique or absolute loudness.

### Guitar-strumming limitations

Do not attempt to infer reliably from microphone audio:

- silent hand movement;
- physical hand-motion direction;
- downstroke versus upstroke direction;
- whether the performer physically crossed exactly the thick or thin three-string group.

For strumming, the reliable analysis target is the timing/presence of audible actions rather than reconstruction of the complete hand-motion instruction.

### Non-ML chord comparison

For chord-change exercises, allow conventional DSP-based comparison against the expected/configured chord set without introducing ML.

A possible processing model is:

```text
PCM
 ↓
frequency-domain analysis
 ↓
pitch-class / chroma representation
 ↓
comparison with expected chord templates
```

Limit recognition to the expected/configured chord set rather than attempting unrestricted transcription of arbitrary music.

Use confidence thresholds.

If the signal cannot be classified with sufficient confidence, return an uncertain/unrecognized result rather than a false definitive error.

Chord recognition is less reliable than onset/timing analysis and must be validated with representative real guitar recordings before it is treated as authoritative feedback.

### Future non-ML pitch analysis

Monophonic pitch detection using conventional autocorrelation/YIN-style techniques is compatible with the same browser pipeline.

Keep this deferred while the scale trainer remains only an idea.

### Metronome leakage

The microphone may capture MelodyTrack's own metronome when it is played through speakers.

Analysis mode should recommend headphones.

Do not assume browser echo cancellation can reliably distinguish the application's metronome from musical attacks.

### Local recording

Record the microphone stream using `MediaRecorder`.

Collect recorded chunks into a `Blob`.

Do not upload the Blob to Backend.

Persist recordings locally using the existing IndexedDB/Dexie frontend persistence layer so they can be replayed later in the same browser/device.

Keep only the browser-local metadata required to associate a recording with its practice session and analysis results.

Users must be able to delete local recordings.

Recordings are device/browser-local and may disappear if the browser's site storage is cleared.

### Playback with audible evaluation overlay

Allow locally stored recordings to be replayed directly inside MelodyTrack.

Keep the original recording unchanged.

Do not bake evaluation sounds into the recorded Blob.

Store analysis events separately and, during playback, optionally overlay short diagnostic sounds for errors such as:

- early attack;
- late attack;
- missed expected attack;
- unexpected/extra attack.

The user must be able to disable the evaluation overlay and listen to the untouched recording alone.

Schedule the recording and diagnostic sounds against the same browser audio clock so the audible markers stay aligned with the recorded performance.

The playback UI should remain compatible with synchronized visual playhead/error highlighting.

### Privacy boundary

This stage does not introduce:

- server-side audio storage;
- audio uploads;
- cloud transcription;
- ML inference;
- background microphone capture.

Microphone access is active only for an explicitly started recording/analysis session.

Normal non-audio practice events required by later practice tracking/scoring remain a separate concern; raw audio must not become part of those server events.

### Integration with trainers

The same analysis pipeline must work wherever the trainer runtime is used:

- homework;
- client portal;
- primary authenticated MelodyTrack application.

Trainer runtimes provide the expected musical-event timeline.

The analysis subsystem consumes that timeline and reports observed timing/results back to the trainer UI.

### Done looks like

- A user can explicitly enable microphone analysis for a supported trainer.
- Timing feedback is calculated entirely in the browser without ML.
- Rhythm ties are evaluated with correct onset semantics.
- A user's performance can be recorded, stored locally, and replayed later on the same device.
- Playback can overlay synchronized audible error markers without modifying the original recording.
- Raw recordings never leave the browser.
- Strumming analysis does not pretend to infer physical hand movement from audio.
- The architecture can optionally adopt WASM DSP later without changing the surrounding React application.

---

## Stage 33: Scoring Model

Leaderboards should be based on a dedicated scoring domain rather than calculated directly from the current state of courses, homework, or trainer screens.

### Remaining scope

- Introduce a scoring flow conceptually based on:

  - learning activity;
  - scoring rules;
  - score events/ledger;
  - aggregations;
  - leaderboard projections.
- Keep the model capable of supporting the score sources already discussed:

  - homework completion;
  - course progress;
  - lesson completion;
  - trainer practice;
  - consistency/streaks;
  - achievements;
  - special events.
- Keep scoring separate from educational completion.
- Preserve teacher-controlled completion.
- Do not let trainer activity automatically mark course content complete.

### Deferred product work

- Not all possible score sources need to be implemented immediately.
- Achievements are not introduced here.
- Practice-specific scoring is integrated later in Stage 36.
- Leaderboard presentation is handled in Stage 34.

### Integration with the refactored architecture

- Keep score history explicit and auditable.
- Avoid deriving competitive ranking directly from mutable UI state.
- Integrate course-related points without changing teacher-controlled completion rules.

### Done looks like

- MelodyTrack has a single scoring source suitable for leaderboard aggregation.
- Score changes can be explained and audited.
- Competitive points remain separate from course mastery.

---

## Stage 34: Leaderboards

The portal should support competitive rankings across courses and time periods using public fantasy aliases.

### Remaining scope

- Implement:

  - course leaderboard;
  - global leaderboard across all courses;
  - monthly leaderboard;
  - yearly leaderboard.
- Display:

  - rank;
  - player alias;
  - score.
- Use `User.PortalAlias` as the public identity.
- Do not expose real client names in leaderboard UI.
- Make the current client's position visible even if they are outside the currently displayed top entries.
- Present leaderboards through the RPG portal design system.

### Deferred product work

- Achievements are not required.
- Historical rank-movement indicators are not required unless separately added.
- Dynamic scoring expansion continues in later stages.

### Integration with the refactored architecture

- Build leaderboard views from the scoring domain rather than directly from course state.
- Respect client authorization boundaries.
- Reuse the portal alias and RPG component infrastructure.

### Done looks like

- Clients can see rankings within a course, globally, monthly, and yearly.
- Public identities use aliases only.
- A client can always understand their own current position.

---

## Stage 35: Portal Home Gamification Integration

The portal home should become the player's central hub once scoring and leaderboard data are available.

### Remaining scope

- Extend the existing Stage 23 home page with:

  - current courses;
  - current homework;
  - trainer access;
  - player alias;
  - relevant leaderboard positions;
  - available progress information.
- Keep the existing home page structure rather than rebuilding it again.
- Present the new gamification data using the RPG design system.

### Deferred product work

- Achievements are not required.
- Additional profile cosmetics are not required.
- Practice-specific score integration is handled separately.

### Integration with the refactored architecture

- Consume the scoring and leaderboard data introduced in Stages 33–34.
- Keep course-progress presentation aligned with Stage 24.
- Keep trainer entry points aligned with Stage 31.

### Done looks like

- The portal home serves as a coherent RPG-style hub.
- Clients can see their current learning, practice, and competitive context from one page.

---

## Stage 36: Practice Tracking and Scoring Integration

Practice activity should participate in gamification without becoming equivalent to teacher-controlled course completion.

### Remaining scope

- Record useful practice events such as:

  - trainer used;
  - practice session start;
  - practice session completion;
  - duration;
  - configured BPM;
  - repetitions.
- Avoid collecting high-frequency telemetry without a concrete product purpose.
- Allow practice activity to contribute to the score ledger.
- Keep scoring rules from making trivial repeated actions an unlimited source of points.
- Define anti-farming behavior together with the actual scoring model rather than hardcoding it into trainer components.
- Keep practice history and score separate from teacher-controlled course completion.
- Keep Stage 32 raw audio recordings local; do not upload them as part of practice tracking/scoring.

### Deferred product work

- No automatic homework checking is introduced.
- No automatic mastery inference is introduced.
- No additional telemetry is required unless it supports a concrete future feature.

### Integration with the refactored architecture

- Emit practice-related scoring events through the scoring domain.
- Keep trainer runtime independent from course progress transitions.
- Preserve auditability for point changes.
- Do not make browser-local recording storage a server-side scoring dependency.

### Done looks like

- Homework-based and standalone practice can contribute to score.
- Repeated trivial actions cannot be treated as unlimited progress.
- Practice never automatically completes course content.
- Practice scoring does not require uploading locally recorded audio.

---

## Stage 37: RPG Experience Polish

The completed portal should receive a final interaction and visual polish pass after the major functional systems are present.

### Remaining scope

Refine:

- portal navigation;
- page transitions;
- homework interaction;
- exercise start/stop states;
- exercise completion states;
- alias selection;
- leaderboard transitions;
- progress presentation;
- loading states;
- empty states;
- error states;
- responsive behavior.

Add the stronger magical and RPG-style animation treatment discussed for the redesign.

Keep motion consistent with the Stage 22 design system.

Respect reduced-motion preferences.

### Deferred product work

- Do not introduce unrelated new product mechanics during polish.
- Sound should not autoplay merely as decoration.

### Integration with the refactored architecture

- Polish existing shared portal components rather than introducing one-off page effects.
- Keep accessibility and responsive behavior intact while adding animation.

### Done looks like

- The portal feels like one coherent fantasy RPG interface.
- Animations support the experience rather than obscuring functionality.
- Reduced-motion users retain a complete usable experience.

---

## Stage 38: Final Legacy Cleanup

The redesign should finish by removing obsolete client-portal infrastructure once all replacement flows are operational.

### Remaining scope

- Remove remaining:

  - old portal routes;
  - old portal UI;
  - PIN-related code;
  - obsolete session assumptions;
  - obsolete portal-authentication endpoints;
  - temporary compatibility code;
  - dead portal components.
- Verify the permanent portal-link flow.
- Verify repeated use of the same portal link.
- Verify multiple client profiles in one browser.
- Verify alias generation and uniqueness.
- Verify structured homework and interactive exercises.
- Verify shared metronome behavior.
- Verify standalone trainers in both application surfaces.
- Verify browser audio-analysis/local-recording boundaries.
- Verify course progress.
- Verify scoring and all leaderboard scopes.

### Deferred product work

- Alias diversity weighting remains deferred to Stage 39.
- No new product functionality should be introduced during cleanup.

### Integration with the refactored architecture

- BBCode and SCEditor should already have been removed during Stage 25.
- PIN authentication should already have been removed during Stage 20.
- This stage should remove only remaining legacy and transitional code.

### Done looks like

- Only the new client portal architecture remains in active use.
- No obsolete authentication, UI, or rendering path is required for normal portal operation.
- Trainer behavior is shared rather than duplicated across portal/main-application surfaces.
- All major client flows have focused verification coverage.

---

## Stage 39: Alias Generation Diversity Improvements — Optional follow-up

If production usage shows that clients repeatedly see the same alias candidates, the generator can later improve diversity automatically without introducing administrator-configured weights.

### Remaining scope

- Persist enough information about generated alias candidates to reconstruct generation frequency.
- Track generated candidates, not only aliases eventually selected by users.
- Record which:

  - epithet;
  - creature or bardic term;
  - exact pair
    was generated.
- Derive dynamic selection weights automatically from previous generation frequency.
- Reduce the probability of:

  - frequently generated epithets;
  - frequently generated creatures;
  - frequently generated bardic terms;
  - exact combinations already shown repeatedly.
- Increase the relative probability of rarely generated words and combinations.
- Do not permanently ban an old combination only because it has appeared before.
- Continue excluding aliases already permanently assigned to users.
- Keep database uniqueness of `User.PortalAlias` as the final duplicate protection.
- Maintain aggregated counters for:

  - individual epithets;
  - individual creatures;
  - individual bardic terms;
  - exact pairs.
- Keep aggregated generator state in memory for fast candidate selection.
- Rebuild the in-memory counters from persisted generation history on application startup.
- Update the counters after each generated candidate.

A possible weighting model is:

```text
wordWeight =
    1 / sqrt(
        (1 + epithetGenerationCount)
        *
        (1 + nounGenerationCount)
    )

pairWeight =
    1 / (1 + pairGenerationCount)

finalWeight =
    wordWeight * pairWeight
```

The exact formula may be adjusted during implementation as long as the intended behavior remains the same.

### Deferred product work

- Do not add manual `Weight` fields to dictionary tables.
- Do not expose dynamic weights in the administrative UI.
- Administrators continue to manage only:

  - `Value`;
  - `IsEnabled`.

### Integration with the refactored architecture

- Reuse the alias dictionaries from Stage 21.
- Keep generation-history persistence separate from dictionary management.
- Avoid querying and aggregating the complete history on every `Another name` request.
- Treat dynamic weighting as an internal generator optimization.

### Done looks like

- Alias generation automatically favors less frequently shown words and combinations.
- Repeated candidates become noticeably less common.
- Administrators do not need to manually tune probabilities.
- Existing alias uniqueness behavior remains unchanged.

---

# Dependency Summary

Use the following order where stages are on the same dependency chain. Deferred independent work does not block the client portal redesign unless a concrete implementation dependency is discovered and documented.

```text
COMPLETED REFACTOR
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

COMPLETED POST-REFACTOR PRODUCT WORK
Stage 11 God mode + system notices
   ↓
Stage 12 Notification infrastructure + Web Push
   ↓
Stage 13 Vacation requests + superuser approval

CURRENT / DEFERRED GENERAL PRODUCT WORK
Stage 14 Calendar workflow + income forecast improvements

Stage 15 Services Progress
   └─ merged into Stage 24; do not implement separately

Stage 16 Multiple staff accounts in one browser — planned for later
   ↓
Stage 17 Offline-first architecture — planned for later

Stage 18 Accounting/staff compensation architecture — planned for later

CLIENT PORTAL REDESIGN
Stage 20 Portal architecture + access foundation
   ↓
Stage 21 Player identity + alias dictionaries
   ↓
Stage 22 RPG design system
   ↓
Stage 23 New portal shell + home
   ↓
Stage 24 Course progress integration + hardening
   ↓
Stage 25 Structured homework documents
   ↓
Stage 26 Custom homework document nodes
   ↓
Stage 27 Shared practice platform architecture
   ↓
Stage 28 Guitar strumming trainer
   ↓
Stage 29 Chord-change + rhythm trainers
   ↓
Stage 30 Shared metronome + exercise timing
   ↓
Stage 31 Shared trainer catalogue + cross-surface availability
   ↓
Stage 32 Browser audio analysis + local practice recording
   ↓
Stage 33 Scoring model
   ↓
Stage 34 Leaderboards
   ↓
Stage 35 Portal home gamification integration
   ↓
Stage 36 Practice tracking + scoring integration
   ↓
Stage 37 RPG experience polish
   ↓
Stage 38 Final legacy cleanup
   ↓
Stage 39 Alias generation diversity improvements — optional follow-up
```

Stages 16–18 remain deliberately deferred and may be reprioritized independently. Stage 17 extends the account-boundary work of Stage 16 for offline state. Stage 18 is an accounting-domain discovery/architecture track.

---
