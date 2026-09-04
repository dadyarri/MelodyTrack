---
name: melodytrack-api-contract
description: Implement or review MelodyTrack native Minimal API contracts and generated Kiota client changes. Use when adding or changing endpoints, request validation, Problem Details, OpenAPI metadata, generated frontend models, or contract-build behavior; do not use for frontend-only API wrapper work.
---

# MelodyTrack API Contract

Work from the MelodyTrack repository root. Read [the repository guidance](../../../AGENTS.md) and [the backend guidance](../../../MelodyTrack.Backend/AGENTS.md) first. Treat the current implementation and tests as the source of truth; use Stage 6 of [the roadmap](../../../roadmap.md) for target behavior that has not landed yet, not as evidence that a migration is already complete.

## Inspect the contract path

Before changing a contract, inspect the smallest relevant set of files:

- endpoint attribute and registration: [ApiEndpointAttribute.cs](../../../MelodyTrack.Backend/Api/ApiEndpointAttribute.cs) and [ApiEndpointGenerator.cs](../../../MelodyTrack.Api.Generators/ApiEndpointGenerator.cs);
- request/response types and the owning endpoint under [the API directory](../../../MelodyTrack.Backend/Api/);
- validation: [the native validation directory](../../../MelodyTrack.Backend/Validation/) and nearby request examples;
- errors: [the error-handling directory](../../../MelodyTrack.Backend/ErrorHandling/) and Problem Details setup in [Program.cs](../../../MelodyTrack.Backend/Program.cs);
- OpenAPI: [the OpenAPI directory](../../../MelodyTrack.Backend/OpenApi/) and [ApiContractTests.cs](../../../MelodyTrack.Backend.Tests/ApiContractTests.cs);
- generation: [Frontend.targets](../../../build/Frontend.targets), [Directory.Solution.targets](../../../Directory.Solution.targets), [dotnet-tools.json](../../../dotnet-tools.json), and the contract lane in [ci.yml](../../../.github/workflows/ci.yml);
- generated output: the `generated/` child of [the shared API directory](../../../MelodyTrack.Web/src/shared/api/) once generation has created it.

## Preserve the end-to-end contract

- A target endpoint is a class with `[ApiEndpoint(ApiMethod, route)]` and exactly one `public static HandleAsync`. Use ordinary Minimal API binding and dependency injection, typed results where practical, and propagate `CancellationToken`.
- The generated endpoint name is also the stable OpenAPI operation ID. Treat endpoint class renames as contract changes and check generated client call sites.
- Express simple validation on request models and use the project native validation boundary for cross-property or service-backed rules. Preserve Russian field messages and validation field keys. Do not introduce a second validation framework.
- Return expected failures through the established error factory/Problem Details path. Preserve status, external error code, field errors, conflict/idempotency/security meaning, and canonical trace ID. Never expose exception internals, secrets, or PII.
- Describe auth, idempotency headers, Problem Details, and non-success responses in endpoint metadata or the existing OpenAPI transformer only when native inference cannot express them. Avoid endpoint-specific document hacks.
- Keep generated Kiota models authoritative for server DTOs. Do not add handwritten TypeScript mirrors and never hand-edit generated files.
- If pagination changes, preserve the canonical `items` plus nested `page` body shape throughout endpoint response, OpenAPI, generated client, wrappers, and tests.

## Generation and verification

Respect the conditions in `build/Frontend.targets`: generation must not recurse during restore, design-time, or explicitly skipped contexts. A repository-root contract build regenerates the client in place and typechecks it; CI rejects stale committed output.

After an authorized contract change, prefer focused generator, endpoint, validation, and `ApiContractTests` checks before the full root build. Inspect the generated diff and any application-facing frontend wrappers. Follow the repository verification policy; this skill does not itself authorize builds, tests, formatting, or generated-file updates outside the requested work.
