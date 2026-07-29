# Releases

`MelodyTrack.Backend/changelog.json` is the only source of the public product version. Its first entry is current. Keep entries newest first and put deployment-only instructions elsewhere.

An actual release uses `yyyy.mm.releaseNumber` and has a manually chosen `codename`. A patch adds `.patchNumber`, omits `codename`, and inherits it from the matching three-part parent entry. Every entry has a `yyyy-MM-dd` date and all four change arrays; at least one change is required. `changelog.schema.json`, backend startup, and release automation reject unknown fields.

## Preparing a release

1. Add and review the newest changelog entry on the backend source branch. Frontend-only changes still require a backend changelog entry.
2. Make both source worktrees clean and ensure they merge into current `origin/master` without conflicts.
3. From the backend repository run `dotnet run scripts/ReleaseTool.cs -- prepare`. It validates both repositories, creates and tests `release/<version>` branches, pushes them, and opens matching pull requests. It never chooses a codename or publishes a release.
4. Review and merge both pull requests normally. Their title must be the exact version and their body must remain the generated changelog.

Backend tests and frontend `npm run verify` run before anything is pushed. If local verification fails, the script restores both source branches and removes only local release branches created by that run. If a remote operation partially succeeds, inspect the printed state before retrying; never delete a published tag or reuse its version.

After a release PR reaches `master`, each repository verifies and publishes its own image. Only then does its workflow create the annotated `v<version>` tag and GitHub Release. Ordinary master changes produce no release. Re-running a completed workflow accepts only a tag at the same merge commit; conflicting tags fail.

For a hotfix, add a four-part patch entry above its existing parent. Backend-only and frontend-only releases use the same two-PR process so product versioning stays unified. Rolling back the backend image also rolls back the served version and notes; record the rollback operationally and publish a new patch before moving forward.
