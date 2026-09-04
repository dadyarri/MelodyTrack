# Repository Guidelines

## Project Structure

This repository is the MelodyTrack monorepo:

- `MelodyTrack.Backend/`: .NET 10 ASP.NET Core application.
- `MelodyTrack.Core/`: EF-free domain entities and abstractions.
- `MelodyTrack.Data/`: EF Core persistence, migrations, and reusable database initialization.
- `MelodyTrack.Init/`: database initialization executable with production, development, and test modes.
- `MelodyTrack.AppHost/`: Aspire development orchestrator.
- `MelodyTrack.ServiceDefaults/`: shared service discovery, health, resilience, and development telemetry defaults.
- `MelodyTrack.Api.Generators/`: analyzer-only Minimal API endpoint source generator.
- `MelodyTrack.Api.Generators.Tests/`: source-generator diagnostics and mapping tests.
- `MelodyTrack.Backend.Tests/`: xUnit integration tests.
- `MelodyTrack.Web/`: Vite, React, and TypeScript frontend.
- `changelog/releases/`: application-level release metadata, one JSON file per release.
- `scripts/`: repository-wide development and release tooling.

The root `MelodyTrack.slnx` is the .NET solution/build entry point. Backend-specific guidance lives in `MelodyTrack.Backend/AGENTS.md`; frontend-specific guidance lives in `MelodyTrack.Web/AGENTS.md`.

## Commands

- `dotnet restore MelodyTrack.slnx`: restore backend dependencies.
- `dotnet build MelodyTrack.slnx`: build the backend solution.
- `dotnet test MelodyTrack.slnx`: run backend tests; Docker is required for Testcontainers.
- `dotnet run --project MelodyTrack.Backend`: run the API.
- `dotnet run --project MelodyTrack.Init -- --mode development`: migrate and seed the development database before running the API.
- `dotnet run --project MelodyTrack.AppHost`: start PostgreSQL, Init, Backend, Vite, and the Aspire Dashboard for local development.
- Run `npm install` and `npm run dev` from `MelodyTrack.Web/` to start Vite.
- Run `npm run verify` from `MelodyTrack.Web/` for the existing frontend verification baseline.
- Run `dotnet run scripts/ReleaseTool.cs -- <command>` from the repository root for release metadata and release workflow operations.

## Workflow

- Preserve behavior unless `roadmap.md` explicitly changes it.
- Keep migration changes bisectable and do not leave two permanent implementations.
- Do not introduce speculative architecture or compatibility layers.
- Propagate cancellation through endpoint, EF, and outbound I/O operations.
- Never expose secrets or personal data in logs, traces, metrics, error metadata, or generated artifacts.
- Do not hand-edit generated EF migration designer files; create migrations with `dotnet ef migrations add`.

Always request an elevated shell before running `dotnet restore`, `dotnet build`, `dotnet test`, `dotnet tool`, or any other `dotnet` command that may access the internet, including commands that can perform an implicit restore. Do not try the command in the restricted sandbox first. If elevation is denied or unavailable, stop and report the blocker immediately; do not pursue browser downloads, alternate network paths, dependency downgrades, or cache substitutions unless the user explicitly asks for that workaround.

Do not run automated tests, builds, linters, formatters, or verification pipelines after intermediate edits. Run verification only when the user explicitly requests it or says the current change batch is complete and ready for verification. When frontend verification is authorized, use `npm run verify:fix` from `MelodyTrack.Web/` and inspect its mutations before committing.

## Test Conventions

- New .NET test files and classes use `<Subject>Tests`; keep one primary subject or behavioral boundary per class. Test methods use `Operation_Context_ExpectedOutcome`, omitting the context segment only when the scenario remains unambiguous.
- Use `[Fact]` for one scenario and `[Theory]` only when the same behavior is exercised over data variants. Do not combine unrelated cases merely to reduce the number of tests.
- Colocate frontend tests with their source. Use `.test.ts`/`.test.tsx` for jsdom/unit behavior, `.browser.test.tsx` for real-browser behavior, and `.webkit.test.tsx` when the behavior specifically carries WebKit risk.
- Frontend suites use `describe` for the subject/capability and a present-tense behavioral sentence in `it`.
- Separate Arrange, Act, and Assert with blank lines when useful; do not add phase comments when the structure is already clear. Assert observable behavior and state, not incidental implementation details.
- Tests must be deterministic, order-independent, and isolated from shared headers, clocks, mocks, database state, and browser persistence.
- Apply these conventions to new or materially changed tests; do not bulk-rename unrelated legacy tests unless that cleanup is explicitly requested.

## Git and Releases

Keep commits focused with short imperative subjects that describe the concrete change. Do not mention roadmap stages, phases, or milestones in commit messages. Do not amend, rebase, or otherwise rewrite existing commits without explicit permission.

Pull requests should state what changed, which app was affected, how it was verified, and any new environment variables, migrations, Docker requirements, or UI screenshots. The current production backend and frontend images remain separate until the unified-runtime work is complete.
