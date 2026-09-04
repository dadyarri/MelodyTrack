# 1. Goal

Reduce pull-request and master-branch CI wall-clock time for both repositories by parallelizing independent verification, reusing dependency/build outputs safely, and preserving the complete existing verification and release gates.

# 2. Approach

Use a fastest-feedback design, as selected: spend additional GitHub-hosted runner minutes to shorten the critical path. The frontend will fan out static analysis, unit tests, Chromium tests, WebKit tests, and production-build checks; the backend will build once, distribute the compiled test module, and fan out unit tests plus two isolated integration-test shards. The backend sharding will use the official xUnit v3 Microsoft Testing Platform query-filter mechanism documented by [xunit.net](https://xunit.net/docs/query-filter-language), so each integration process starts its own Testcontainers PostgreSQL instance rather than sharing mutable database state.

Keep the current NuGet and npm caches because measured runs show both hit reliably; do not cache generated build output across commits or cache the frontend dependency directory. Add a dedicated WebKit binary cache and BuildKit package-download mounts, where lock/version keys make staleness explicit. Preserve named aggregate jobs for branch protection and require all lanes before image/release publication.

# 3. File Changes

## Backend repository

- **Modify** [main.yml](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/.github/workflows/main.yml?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A250%2C%22second%22%3A2963%7D%2C%22lines%22%3A%7B%22first%22%3A17%2C%22second%22%3A83%7D%7D&root=%252F) (current lines 18–84): replace the serial build/test lane with parallel release validation and compilation, upload the compiled test module once, run a three-entry test matrix, add an aggregate gate, and pass release-version output into the existing publish job.
- **Modify** [global.json](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/global.json?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A0%2C%22second%22%3A110%7D%2C%22lines%22%3A%7B%22first%22%3A0%2C%22second%22%3A7%7D%7D&root=%252F) (lines 1–8): select Microsoft Testing Platform as the .NET 10 test runner, enabling xUnit v3 query filters and direct compiled-module execution.
- **Modify** [IntegrationTestCollection.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend.Tests/Infrastructure/IntegrationTestCollection.cs?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A0%2C%22second%22%3A257%7D%2C%22lines%22%3A%7B%22first%22%3A0%2C%22second%22%3A6%7D%7D&root=%252F) (lines 1–7): add a collection-level integration trait while retaining disabled in-process parallelization for the shared fixture.
- **Modify** [xunit.runner.json](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend.Tests/xunit.runner.json?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A0%2C%22second%22%3A254%7D%2C%22lines%22%3A%7B%22first%22%3A0%2C%22second%22%3A8%7D%7D&root=%252F) (lines 1–9): allow safe unit-test collections to run in parallel; the integration and startup-configuration collections remain explicitly non-parallel.
- **Modify** [Dockerfile](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend/Dockerfile?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A0%2C%22second%22%3A876%7D%2C%22lines%22%3A%7B%22first%22%3A0%2C%22second%22%3A23%7D%7D&root=%252F) (lines 1–24): mount a BuildKit NuGet download cache around restore so package-version changes reuse unchanged downloads while retaining the existing project-file layer boundary.
- **Modify** [AGENTS.md](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/AGENTS.md?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A1057%2C%22second%22%3A1211%7D%2C%22lines%22%3A%7B%22first%22%3A21%2C%22second%22%3A30%7D%7D&root=%252F) (lines 22–31): update the documented .NET 10 test invocation to the named-solution MTP syntax.

## Frontend repository

- **Modify** [frontend-ci.yml](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Web/.github/workflows/frontend-ci.yml?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A440%2C%22second%22%3A3666%7D%2C%22lines%22%3A%7B%22first%22%3A25%2C%22second%22%3A131%7D%7D&root=%252F) (current lines 26–132): retain the uncommitted WebKit split, fan the remaining verification into independent lanes, cache Playwright WebKit binaries, add a named aggregate verification gate, and make publish depend on that gate.
- **Modify** [package.json](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Web/package.json?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A107%2C%22second%22%3A1588%7D%2C%22lines%22%3A%7B%22first%22%3A6%2C%22second%22%3A32%7D%7D&root=%252F) (current lines 7–33): retain the existing uncommitted primary/WebKit split, add stable scripts for static checks, unit tests, Chromium tests, and bundle verification, and separate the Vite-only bundle command from the public typechecked build command to avoid duplicate typechecking in the aggregate verification.
- **Modify** [Dockerfile](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Web/Dockerfile?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A0%2C%22second%22%3A554%7D%2C%22lines%22%3A%7B%22first%22%3A0%2C%22second%22%3A17%7D%7D&root=%252F) (lines 1–18): mount a BuildKit npm download cache around the locked install so lockfile changes reuse unchanged tarballs without persisting the dependency directory.

No files are created or deleted.

# 4. Implementation Steps

## Task 1: Make frontend verification independently runnable

1. In [package.json](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Web/package.json?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A107%2C%22second%22%3A1588%7D%2C%22lines%22%3A%7B%22first%22%3A6%2C%22second%22%3A32%7D%7D&root=%252F), preserve the current public commands, introduce a Vite-only bundle command, and define four CI-addressable groups: static/type checks, unit tests, Chromium browser tests, and production bundle plus budget check.
2. Recompose the local primary verification command from those groups and keep the full verification command as primary plus WebKit. This preserves one-command local verification while allowing CI fan-out and ensures every existing check remains represented exactly once except the intentionally typechecked public build.
3. In [frontend-ci.yml](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Web/.github/workflows/frontend-ci.yml?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A440%2C%22second%22%3A3666%7D%2C%22lines%22%3A%7B%22first%22%3A25%2C%22second%22%3A131%7D%7D&root=%252F), create parallel jobs for static/security/audit checks, unit tests, Chromium tests, bundle verification, and WebKit tests. Give each job the same pinned checkout/setup-node sequence, npm cache key, locked install, timeout, and API-base environment.
4. Add an always-evaluated aggregate job named “verify” which fails if any required lane failed or was cancelled. Point the publish job at this aggregate gate so no image or release is produced from partial verification, while preserving a stable branch-protection check name.

## Task 2: Cache the expensive frontend browser setup

1. In the WebKit lane in [frontend-ci.yml](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Web/.github/workflows/frontend-ci.yml?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A440%2C%22second%22%3A3666%7D%2C%22lines%22%3A%7B%22first%22%3A25%2C%22second%22%3A131%7D%7D&root=%252F), add a SHA-pinned cache action for the default Playwright browser directory. Key it by runner OS and lockfile hash; add an OS-scoped restore prefix so a Playwright update can reuse any still-valid browser payload before installing the new revision.
2. Always install/check WebKit system dependencies, but download the browser binary only on a non-exact cache hit. Keep the Playwright version sourced from the locked dependency rather than introducing a second version constant.
3. Do not cache the dependency directory, Vite output, or TypeScript build-info files across commits; the measured npm tarball cache already makes a clean locked install approximately eight seconds and avoids stale generated-state failures.

## Task 3: Enable safe backend test selection and local parallelism

1. In [global.json](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/global.json?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A0%2C%22second%22%3A110%7D%2C%22lines%22%3A%7B%22first%22%3A0%2C%22second%22%3A7%7D%7D&root=%252F), configure the .NET 10 test runner as Microsoft Testing Platform. Keep the pinned SDK and roll-forward policy unchanged.
2. Add an integration category trait to [IntegrationTestCollection.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend.Tests/Infrastructure/IntegrationTestCollection.cs?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A0%2C%22second%22%3A257%7D%2C%22lines%22%3A%7B%22first%22%3A0%2C%22second%22%3A6%7D%7D&root=%252F), retaining the collection’s explicit no-parallel flag so a single process never runs shared-database tests concurrently.
3. Set collection parallelization on in [xunit.runner.json](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend.Tests/xunit.runner.json?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A0%2C%22second%22%3A254%7D%2C%22lines%22%3A%7B%22first%22%3A0%2C%22second%22%3A8%7D%7D&root=%252F). Unit-test collections may then use available cores, while the integration collection and the existing environment-mutating startup collection remain serialized.
4. Update [AGENTS.md](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/AGENTS.md?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A1057%2C%22second%22%3A1211%7D%2C%22lines%22%3A%7B%22first%22%3A21%2C%22second%22%3A30%7D%7D&root=%252F) so contributors use the .NET 10 MTP named-solution form and do not copy the former positional VSTest invocation.

## Task 4: Build the backend once and shard test execution

1. Refactor [main.yml](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/.github/workflows/main.yml?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A250%2C%22second%22%3A2963%7D%2C%22lines%22%3A%7B%22first%22%3A17%2C%22second%22%3A83%7D%7D&root=%252F) into concurrent release-metadata and compile jobs. The metadata job validates the changelog and exposes the current release version; the compile job retains setup-dotnet’s NuGet cache, restores once, performs the Release build once, and uploads only the compiled test/runtime outputs needed by the test hosts.
2. Add a matrix test job that checks out source files required by the fixture, installs the SDK without restoring NuGet packages, downloads the compiled output, and invokes the built MTP test module directly.
3. Define three mutually exclusive filters:
   - unit tests: tests without the integration trait;
   - integration shard A: integration classes whose names begin A through M;
   - integration shard B: integration classes whose names begin N through Z.
   The alphabetical prefix sets cover the entire class-name space, automatically place future integration classes into one shard, and avoid maintaining a hard-coded class list.
4. Keep one PostgreSQL Testcontainer per integration matrix process. Do not point multiple shards at a shared service container or enable parallel execution within the integration collection.
5. Add an always-evaluated aggregate “build-and-test” job that requires release validation, compilation, and all matrix entries. Make the Docker publication job require this gate and consume the metadata job’s release-version output, eliminating the second pre-build version-read invocation while retaining the final release publication command.

## Task 5: Improve cold and invalidated Docker builds

1. In the backend [Dockerfile](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend/Dockerfile?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A0%2C%22second%22%3A876%7D%2C%22lines%22%3A%7B%22first%22%3A0%2C%22second%22%3A23%7D%7D&root=%252F), use a BuildKit cache mount for the NuGet global-packages directory during restore. Keep restore before the source copy and keep publish in Release mode with the existing informational version.
2. In the frontend [Dockerfile](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Web/Dockerfile?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A0%2C%22second%22%3A554%7D%2C%22lines%22%3A%7B%22first%22%3A0%2C%22second%22%3A17%7D%7D&root=%252F), use a BuildKit cache mount for npm’s download cache during the locked install. Keep the dependency-manifest copy before the source copy and retain the typechecked public build.
3. Retain the existing GitHub Actions Buildx cache export in both workflows because cache mounts require max-mode export to survive hosted-runner replacement. Preserve provenance, SBOM generation, tags, and release behavior.

# 5. Acceptance Criteria

1. A pull request starts all five frontend verification lanes concurrently after checkout/setup; publication remains skipped.
2. The frontend aggregate “verify” check succeeds only when static/security/audit checks, unit tests, Chromium tests, WebKit tests, the production build, and the bundle budget all succeed.
3. A second frontend run with an unchanged lockfile reports exact hits for both npm and WebKit browser caches; WebKit system dependency validation still executes.
4. The local full frontend verification still invokes every check present before this change, including WebKit, exactly once; the public production build still performs strict typechecking.
5. Backend compilation occurs once per workflow run, and all three test matrix entries execute the downloaded compiled module without restore or rebuild.
6. Backend test discovery partitions every test into exactly one lane: the unit filter excludes the integration trait, and the A–M/N–Z integration filters are disjoint and exhaustive.
7. Each backend integration shard starts and disposes its own PostgreSQL Testcontainer; no database or mutable fixture is shared between matrix jobs.
8. The unfiltered backend run and the sum of the three filtered discovery manifests contain the same test IDs with no missing or duplicate IDs. At the current baseline this includes all 342 tests, while the comparison remains valid as tests are added.
9. Unit-test collections may execute in parallel, but the integration and startup-configuration collections remain serialized within a process.
10. The backend aggregate “build-and-test” check fails when release validation, compilation, or any test shard fails or is cancelled.
11. Master publication still emits the latest, commit-SHA, and release-version image tags with provenance and SBOM, and publishes release notes only after the aggregate verification gate passes.
12. Across three comparable cache-warm pull-request runs, median frontend wall-clock time is at least 35% below the measured 3:27 representative baseline, and median backend wall-clock time is at least 25% below the measured 2:07 representative baseline.
13. A Docker build after a package-manifest change reuses unchanged NuGet/npm downloads through BuildKit cache mounts; builds with unchanged manifests retain their existing dependency-layer cache hits.

# 6. Verification Steps

Verification is deferred until the user explicitly authorizes it for the completed change batch, per repository policy.

1. Backend configuration check: confirm the SDK selected by [global.json](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/global.json?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A0%2C%22second%22%3A110%7D%2C%22lines%22%3A%7B%22first%22%3A0%2C%22second%22%3A7%7D%7D&root=%252F), restore and build [MelodyTrack.slnx](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.slnx?type=file&root=%252F), then run the solution with the .NET 10 MTP named-solution form.
2. Backend partition check: list test IDs once without a filter and once for each of the unit, A–M integration, and N–Z integration queries; sort and compare the manifests to prove set equality and zero intersection before executing the filtered lanes.
3. Backend behavior check: execute all three filtered lanes, confirm both integration lanes independently create PostgreSQL containers, and then execute one unfiltered run to catch any runner/configuration behavior difference.
4. Frontend check: run the repository-prescribed mutating verification command, `npm run verify:fix`, once the batch is declared ready; confirm all aggregate scripts and the production bundle complete.
5. Docker check: build each image twice with BuildKit enabled, then change only the relevant package-manifest input in a disposable branch and confirm the package-download mount is reused while the dependency installation layer is correctly recomputed.
6. GitHub Actions check: open a test pull request in each repository, inspect the job graph and cache annotations, deliberately fail one lane to confirm the aggregate gate fails, then rerun successfully.
7. Release-path check: after pull-request verification, run or observe one authorized master/dispatch publication and confirm all three tags, provenance, SBOM, and release notes are present.
8. Performance check: record job start/end timestamps for three cache-warm runs before and after rollout and compare medians against Acceptance Criterion 12 rather than relying on a single runner/region sample.

# 7. Risks & Mitigations

- **MTP changes test-runner semantics and command syntax.** Mitigation: retain the current xUnit packages, compare unfiltered discovery IDs before and after, run one full unfiltered suite, and document the .NET 10 named-solution command in [AGENTS.md](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/AGENTS.md?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A1057%2C%22second%22%3A1211%7D%2C%22lines%22%3A%7B%22first%22%3A21%2C%22second%22%3A30%7D%7D&root=%252F). If collection traits are not surfaced in discovered test metadata, stop the rollout and use explicit class-level traits rather than accepting an incomplete shard.
- **Compiled artifact transfer can erase the expected speedup if it is too broad.** Mitigation: upload only Release test/runtime output, not the whole workspace, dependency cache, or container layers; measure upload/download time separately and keep the former single-job path as the fallback.
- **Parallel backend shards consume more runner minutes and pull PostgreSQL more than once.** This is an intentional fastest-feedback tradeoff. Limit the matrix to two integration shards plus one unit lane, and retain cancellation of superseded runs.
- **Frontend fan-out repeats Node setup and locked installation.** This adds runner minutes but only about 14 seconds per lane on measured cache hits. Avoid additional micro-jobs; keep small static checks grouped together.
- **Playwright browser caches can consume quota or become stale.** Key by OS and lockfile, never cache system packages, and let Playwright validate/install the required revision on non-exact hits.
- **Changing job topology can break required-check names.** Preserve explicit aggregate jobs named “verify” and “build-and-test”; confirm repository branch-protection rules against the first test pull requests before merging.
- **Existing frontend work is uncommitted.** Extend the current [frontend-ci.yml](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Web/.github/workflows/frontend-ci.yml?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A440%2C%22second%22%3A3666%7D%2C%22lines%22%3A%7B%22first%22%3A25%2C%22second%22%3A131%7D%7D&root=%252F) and [package.json](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Web/package.json?type=file&linesData=%7B%22range%22%3A%7B%22first%22%3A107%2C%22second%22%3A1588%7D%2C%22lines%22%3A%7B%22first%22%3A6%2C%22second%22%3A32%7D%7D&root=%252F) changes in place and inspect the final diff to ensure no user work is overwritten.
- **Docker cache mounts only help BuildKit builds.** Both publication workflows already use Buildx; keep ordinary local build compatibility as a verification case and do not remove existing manifest-first layer caching.