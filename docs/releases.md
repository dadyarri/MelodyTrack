# Releases

`changelog/releases/` is the application-level source of public release metadata. Each release or hotfix has one file named after its version, such as `2026.08.1.json`; `changelog/release.schema.json` documents the file shape. Release files contain human-written `new`, `improved`, `fixed`, and `security` entries.

A regular release uses `yyyy.mm.releaseNumber` and has a manually chosen `codename`. A hotfix adds `.hotfixNumber`, omits `codename`, and inherits it from the matching regular release. Released entries have a `yyyy-MM-dd` date. The release tool validates filenames, version uniqueness, parent releases, required categories, and non-empty change text.

## Preparing a release

1. Add and review the release file on the source branch.
2. Keep the monorepo worktree clean and ensure it merges into current `origin/master` without conflicts.
3. From the repository root run `dotnet run scripts/ReleaseTool.cs -- prepare`. It validates the monorepo, creates and verifies `release/<version>`, pushes it, and opens one pull request. It never chooses a codename or publishes a release.
4. Review and merge the pull request normally. Its title must be the exact version and its body must remain the generated changelog entry.
5. After the pull request is merged, check out local `develop` and run `dotnet run scripts/ReleaseTool.cs -- finalize`. It fetches `origin/master`, fast-forwards local `master` and then `develop`, and deletes merged local `release/*` branches.

Release preparation runs the existing backend `dotnet test` and frontend `npm run verify` baselines before anything is pushed. If verification fails, the tool restores the source branch and removes only a local release branch created by that run. If a remote operation partially succeeds, inspect the printed state before retrying; never delete a published tag or reuse its version.

Finalization remains deliberately fast-forward-only for the current regular release flow. It refuses dirty worktrees, a local `master` that diverged from `origin/master`, a `develop` branch not contained in the merged remote master, or an unmerged local release branch. Hotfix allocation and merge-back handling are deferred to the later release-tool cleanup described in `roadmap.md`.

After a release pull request reaches `master`, monorepo CI verifies both applications and builds the existing backend and frontend production images. `ReleaseTool` then creates the annotated `v<version>` tag and GitHub Release when the merge is a valid release pull request. Ordinary master changes produce no release. The two production images remain separate until the unified Kestrel runtime cutover.
