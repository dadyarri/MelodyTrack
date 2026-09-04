# Releases

`changelog/releases/` is the application-level source of public release metadata. Each release or hotfix has one file named after its version, such as `2026.08.1.json`; `changelog/release.schema.json` documents the file shape. Release files contain human-written `new`, `improved`, `fixed`, and `security` entries.

A regular release uses `yyyy.mm.releaseNumber` and has a manually chosen `codename`. A hotfix adds `.hotfixNumber`, omits `codename`, and inherits it from the matching regular release. Released entries have a `yyyy-MM-dd` date. The one active draft has `date: null`; Backend validates but does not expose it through release-history endpoints. The release tool validates filenames, version uniqueness, parent releases, required categories, and non-empty released change text.

## Starting the next regular draft

Run from a clean local `develop` branch:

```text
dotnet run scripts/ReleaseTool.cs -- start-next-release <codename> [YYYY.MM.N]
```

Without an override, the tool uses the current UTC year/month and allocates the next unused regular sequence. An override must be a valid unused regular version newer than production. The command writes and commits the new `date: null` draft; codenames and release-note text remain human-authored.

## Preparing a release

1. Add and review entries in the active draft on local `develop`.
2. Keep the monorepo worktree clean and ensure it merges into current `origin/master` without conflicts.
3. Run `dotnet run scripts/ReleaseTool.cs -- prepare <next-codename> [next-version]`. It dates the current draft, creates and verifies `release/<version>`, pushes it, and opens one pull request. After that succeeds it dates the same entry on local `develop`, automatically allocates and commits the next regular draft using the supplied codename (or validated explicit next version).
4. Review and merge the pull request normally. Its title must be the exact version and its body must remain the generated changelog entry.
5. After the pull request is merged, check out local `develop` and run `dotnet run scripts/ReleaseTool.cs -- finalize`. It fetches the release refs, fast-forwards local `master`, merges `master` into local `develop`, and deletes merged local release/hotfix branches.

Release preparation runs the existing backend `dotnet test` and frontend `npm run verify` baselines before anything is pushed. If verification fails, the tool restores `develop` and removes only a local release branch created by that run. If a remote operation partially succeeds, inspect the printed state before retrying; never delete a published tag or reuse its version.

## Hotfixes

From a clean, up-to-date local `master`, run:

```text
dotnet run scripts/ReleaseTool.cs -- start-hotfix
```

The command fast-forwards from `origin/master`, derives the next suffix from the current production regular release (including existing hotfixes), creates `hotfix/<version>`, and commits an empty `date: null` draft. Add urgent fix notes and code on that branch, then run `dotnet run scripts/ReleaseTool.cs -- prepare`. Hotfixes inherit their parent codename and never create the next regular draft.

After either pull request is merged, run `dotnet run scripts/ReleaseTool.cs -- finalize` from clean local `develop`. The command fast-forwards local `master`, merges released master into `develop` even when a hotfix made histories diverge, deletes merged local release/hotfix branches, and merges master into every still-active local `release/*` branch. An unmerged `hotfix/*` branch is left untouched. Merge conflicts stop the command for manual resolution; no history is rewritten.

`dotnet run scripts/ReleaseTool.cs -- self-test` covers regular/hotfix allocation, draft parsing, and merge-back branch decisions. CI runs it with changelog validation.

After a valid `release/*` or `hotfix/*` pull request reaches `master`, monorepo CI verifies both applications and builds the unified `ghcr.io/<owner>/melody-track` production image. It publishes `latest`, the release version, and `sha-<commit>` tags, then creates the annotated Git tag and GitHub Release from the changelog. The final image contains the ASP.NET runtime, Init, Backend, and the compiled SPA, but no Node.js or nginx runtime. Ordinary master changes do not publish or retag an image. Deployment remains a separate manual operation.
