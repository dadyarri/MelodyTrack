# MelodyTrack

MelodyTrack is maintained as one repository with separate backend and frontend development projects.

## Layout

- `MelodyTrack.Backend/` — .NET 10 ASP.NET Core API
- `MelodyTrack.Core/` — EF-free domain abstractions
- `MelodyTrack.Data/` — EF Core context, migrations, persistence, and reusable initialization
- `MelodyTrack.Init/` — database initialization executable
- `MelodyTrack.Backend.Tests/` — xUnit/Testcontainers integration tests
- `MelodyTrack.Web/` — Vite, React, and TypeScript frontend
- `changelog/releases/` — one JSON file per application release
- `scripts/` — repository-wide release and maintenance tooling

## Development

From the repository root, `dotnet build` uses `MelodyTrack.slnx` as the solution entry point. The frontend retains its existing commands during the repository migration:

```text
dotnet restore
dotnet build
dotnet test

dotnet run --project MelodyTrack.Init -- --mode development
dotnet run --project MelodyTrack.Backend

cd MelodyTrack.Web
npm install
npm run dev
npm run verify
```

Docker is required for backend integration tests. See `roadmap.md` for the active migration contract, `docs/releases.md` for the release workflow, and `docs/frontend-verification-inventory.md` for the custom frontend checks that must be preserved or replaced deliberately.
