# Repository Guidelines

## Project Structure

This repository is the MelodyTrack monorepo:

- `MelodyTrack.Backend/`: .NET 10 ASP.NET Core application.
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

Do not run automated tests, builds, linters, formatters, or verification pipelines after intermediate edits. Run verification only when the user explicitly requests it or says the current change batch is complete and ready for verification. When frontend verification is authorized, use `npm run verify:fix` from `MelodyTrack.Web/` and inspect its mutations before committing.

## Git and Releases

Keep commits focused with short imperative subjects that describe the concrete change. Do not mention roadmap stages, phases, or milestones in commit messages. Do not amend, rebase, or otherwise rewrite existing commits without explicit permission.

Pull requests should state what changed, which app was affected, how it was verified, and any new environment variables, migrations, Docker requirements, or UI screenshots. The current production backend and frontend images remain separate until the unified-runtime work is complete.
