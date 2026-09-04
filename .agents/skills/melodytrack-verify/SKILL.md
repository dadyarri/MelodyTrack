---
name: melodytrack-verify
description: Verify a completed MelodyTrack change batch using the smallest sufficient backend, frontend, contract, runtime, documentation, and skill checks. Use when the user explicitly asks to verify, validate, or prove the current repository changes; do not use during intermediate edits or as permission to fix unrelated failures.
---

# MelodyTrack Verify

Verify the completed change batch from the MelodyTrack repository root. This workflow authorizes verification, including the repository's mutating frontend verifier, but not unrelated fixes, commits, pushes, releases, or deployments.

## Establish the exact verification scope

1. Read [the repository guidance](../../../AGENTS.md) and every scoped `AGENTS.md` that applies to changed files.
2. Inspect the branch, staged and unstaged diffs, untracked files, and the last relevant commits. Include verifier-created mutations in the final assessment.
3. Classify the change by affected boundary. Do not run product-wide pipelines for instruction-only changes when focused structural validation proves them.
4. Before running commands, state the selected checks and any unavailable prerequisites such as Docker or browser runtimes.

Treat shared build files, central package/configuration files, solution membership, API contracts, generated client inputs, and runtime hosting as cross-cutting even when their diff is small.

## Respect execution and permission boundaries

- Before any `dotnet` command that can access the internet or restore implicitly, request an elevated shell or the product's expanded network/filesystem permission. Do not try it in the restricted sandbox first. If permission is denied or unavailable, stop that verification lane and report it as blocked.
- Follow current repository commands and project scripts rather than reconstructing long command lines from memory. Keep generated logs, binlogs, test results, and other transient artifacts out of commits.
- Verification may observe and report failures. Do not change product code merely to make a check pass unless the user also asked for fixes.
- `npm run verify:fix` is intentionally mutating. Inspect and report every resulting change; do not silently discard or commit it.
- Docker-backed checks may create only temporary, self-cleaning resources. Do not touch shared or production services.

## Select sufficient checks

Always run `git diff --check` and inspect the final diff. Add lanes according to the affected boundary:

- **Skills or agent guidance only:** run the bundled `skill-creator` `quick_validate.py` for every added or changed skill. Check links and relative paths manually. Product builds and tests are normally unnecessary.
- **Backend, Core, Data, Init, AppHost, ServiceDefaults, generators, or shared MSBuild infrastructure:** restore when required, then build the root `MelodyTrack.slnx`. Run the narrowest relevant .NET tests first; widen to the solution when shared infrastructure or behavior crosses project boundaries. Integration tests require Docker.
- **Frontend source or frontend tooling:** from `MelodyTrack.Web`, run the repository-authorized `npm run verify:fix`. Review its mutations and ensure its type, unit, architecture, browser, security, bundle, and production-build lanes completed as defined by the current script.
- **Native API/OpenAPI/Kiota contract:** perform the backend checks plus the repository-root contract generation/build path. Inspect committed generated-client changes and prove regeneration is deterministic and leaves no stale diff.
- **Unified hosting, Dockerfile, entrypoint, publish layout, health routes, or SPA fallback:** use the focused runtime tests and `scripts/verify-unified-image.sh` when applicable, then confirm temporary containers and images are cleaned up.
- **Documentation or release metadata only:** validate the changed format, links, schemas, or purpose-specific tooling. Do not run a product build without a concrete dependency on the changed content.

When a focused check fails, capture the exact failure and stop widening that lane unless a broader command is needed to distinguish scope. Do not repeatedly rerun the same failing command without a new hypothesis.

## Report evidence

Finish with a compact table or list containing each selected lane, exact command or inspection, and `PASS`, `FAIL`, `BLOCKED`, or `SKIPPED` with a reason. Also report:

- files changed by mutating verification;
- artifacts intentionally left untracked;
- prerequisites that prevented a lane from running;
- whether the exact final working tree is safe to commit.

The result is successful only when every required lane passes and verifier-created changes have been reviewed. Never infer release readiness and never run release tooling from this workflow.
