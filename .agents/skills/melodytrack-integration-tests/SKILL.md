---
name: melodytrack-integration-tests
description: Add, structure, select, or troubleshoot MelodyTrack backend tests, especially integration tests using its WebApplicationFactory, PostgreSQL Testcontainer, real Init pipeline, shared database reset, and native endpoint client. Do not use merely to run an already-selected test command.
---

# MelodyTrack Integration Tests

Work from the MelodyTrack repository root. Read [the repository guidance](../../../AGENTS.md) and [the backend guidance](../../../MelodyTrack.Backend/AGENTS.md), then inspect the test being changed and [the test infrastructure](../../../MelodyTrack.Backend.Tests/Infrastructure/).

## Choose the right test boundary

- Use a plain unit test for deterministic helpers, comparers, renderers, and services whose dependencies can be supplied directly.
- Use `IntegrationTestBase` and the shared `MelodyTrackFixture` when behavior depends on HTTP binding, filters, auth, DI, EF Core, PostgreSQL semantics, initialization, or transactions.
- Do not replace PostgreSQL behavior with an in-memory provider. Normal integration tests use `WebApplicationFactory<Program>`, PostgreSQL Testcontainers, and the real initialization implementation in test mode; Aspire AppHost is not part of this harness.

## Naming and file structure

- Place backend application tests in `MelodyTrack.Backend.Tests` with namespace `MelodyTrack.Backend.Tests`. Use one `<Subject>Tests.cs` file and matching `<Subject>Tests` class per primary subject or behavioral boundary.
- Name test methods `Operation_Context_ExpectedOutcome`, for example `CreateClient_WithDuplicateContact_ReturnsConflict`. Omit the middle segment only when the scenario is still precise, as in `Render_ReplacesSupportedTokens`. Do not use a `Test` method prefix/suffix or vague outcomes such as `Works`.
- Use `[Fact]` for one scenario. Use `[Theory]` plus inline/member data only when every row exercises the same behavior and assertions; do not hide several workflows behind branching test code.
- Keep small test-only factories and failure doubles private at the bottom of the class. Move construction into [TestDataFactory](../../../MelodyTrack.Backend.Tests/Infrastructure/TestDataFactory.cs) only after multiple test classes need the same domain setup.
- Unit test classes have no integration collection or fixture. Integration classes use `[Collection(IntegrationTestCollection.Name)]`, accept `MelodyTrackFixture` through the primary constructor, and normally derive from `IntegrationTestBase`.
- Apply these conventions to new or materially changed tests. Do not bulk-rename unrelated legacy tests unless the task explicitly includes that cleanup.

## Test body conventions

- Structure Arrange, Act, and Assert as visually distinct blocks separated by blank lines when non-trivial. Avoid redundant phase comments.
- Prefer Shouldly in `MelodyTrack.Backend.Tests`. Assert the public result and relevant persisted/audit state; avoid assertions against incidental implementation details.
- Give each test one behavioral reason to fail. Multiple assertions are useful when they prove facets of the same outcome, but split unrelated authorization, validation, and persistence scenarios.
- Use fixed UTC timestamps or controlled `TimeProvider` state whenever time changes the outcome. Avoid live-clock test data near boundaries.
- Use `TestContext.Current.CancellationToken` for async HTTP, EF, stream, and initialization work.
- Tests must not depend on order. Clear shared client headers, restore service/clock overrides, and verify writes with a cleared tracker or fresh scope where tracking could conceal the stored result.

## Follow the harness lifecycle

- Inspect [MelodyTrackFixture.cs](../../../MelodyTrack.Backend.Tests/Infrastructure/MelodyTrackFixture.cs) before assuming setup behavior. It owns the container, configuration, Init invocation, client, database reset, and baseline seed.
- Derive integration test classes from `IntegrationTestBase` unless a test deliberately needs a different lifecycle. Its initialization resets shared state and its disposal clears client headers.
- The fixture preserves selected lookup/migration tables during reset. Do not mutate preserved rows and expect the next test to restore them. Use a genuinely fresh initialization boundary for tests about first-run or initialization failure behavior.
- Use `TestContext.Current.CancellationToken` for test I/O and pass cancellation through helpers.
- Keep headers, authentication state, rate-limit identities, clocks, and service overrides scoped to the test. Restore or clear anything the shared fixture could carry into another test.

## Arrange and call the application

- Reuse [TestDataFactory](../../../MelodyTrack.Backend.Tests/Infrastructure/TestDataFactory.cs) for established entities and recurrence rules instead of copying large setup graphs. Extend it only for patterns that recur across test classes.
- For migrated Minimal APIs, prefer the typed helpers in [NativeApiTestClientExtensions.cs](../../../MelodyTrack.Backend.Tests/Infrastructure/NativeApiTestClientExtensions.cs); they derive routes from `[ApiEndpoint]`, fill route/query values, and serialize request bodies consistently.
- Use raw `HttpRequestMessage` or direct service resolution when the behavior under test specifically requires headers, malformed payloads, streaming, concurrency, transaction failure, or a non-HTTP boundary.
- Assert externally meaningful status, Problem Details/error fields, response data, and persisted state. For transaction-sensitive behavior, verify state from a clean scope or context.

## Focused verification

Select the narrowest test class or filter that proves the change, then widen only when shared infrastructure or a cross-cutting contract changed. Docker must be available for integration tests. Follow the repository verification policy; writing a test does not itself authorize running it or the full solution suite.
