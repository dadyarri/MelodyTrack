---
name: melodytrack-release
description: Prepare, review, start, finalize, or recover MelodyTrack regular releases and hotfixes through scripts/ReleaseTool.cs. Use for release drafts, customer-facing changelogs, release/hotfix branches and pull requests, merge-back, or manual publish recovery; require the user to approve the exact changelog before any release-note commit or prepare operation.
---

# MelodyTrack Release

Operate the monorepo release workflow through `scripts/ReleaseTool.cs`. Read [the repository guidance](../../../AGENTS.md) and [the release workflow](../../../docs/releases.md) before acting. The release tool is authoritative for version allocation, draft creation, release/hotfix branches, verification, pull requests, merge-back, and recovery publishing.

Do not recreate its versioning or branch logic with ad hoc Git commands. The one exception is committing an already approved edit to the active draft's customer-facing release notes, because `prepare` requires a clean worktree.

## Hard changelog review gate

Customer-facing changelog text must be manually verified by the user before it is committed.

- Draft or revise the active `changelog/releases/<version>.json` without staging or committing it.
- Validate it, then show the user the exact version, codename when applicable, and every `new`, `improved`, `fixed`, and `security` entry. Also show or summarize the complete file diff.
- Ask for explicit approval of that exact content. End the turn without committing, running `prepare`, pushing, or opening a pull request.
- Earlier general authorization, silence, or approval given before the final content was shown does not satisfy this gate.
- Any edit after approval invalidates the approval. Show the new exact content and ask again.
- After explicit approval, re-read the file and diff to prove it is unchanged, stage only that draft file, inspect the complete staged diff, and commit it with a focused subject such as `document 2026.09.1 release notes`.
- Do not stage unrelated code or metadata with the changelog. `prepare` requires every other intended product change to be committed separately and the worktree to be clean.

`start-next-release` and `start-hotfix` create and commit an empty `date: null` draft scaffold. This contains no customer-facing notes and is permitted after telling the user that the tool will create that bootstrap commit. The hard review gate applies before any non-empty release-note content is committed and before `prepare` dates or republishes it.

## Inspect state first

Work from the repository root. Inspect:

- `git status --short --branch` and the current branch;
- relevant local and remote release/hotfix branches;
- the active draft, if any;
- `current-version` and `validate` output;
- recent commits and the diff since `origin/master` that may affect users.

Request an elevated shell before every `dotnet` command, as required by the repository. Use these commands rather than modifying release metadata mechanically:

```text
dotnet run scripts/ReleaseTool.cs -- validate
dotnet run scripts/ReleaseTool.cs -- current-version
dotnet run scripts/ReleaseTool.cs -- release-kind
dotnet run scripts/ReleaseTool.cs -- start-next-release <codename> [YYYY.MM.N]
dotnet run scripts/ReleaseTool.cs -- start-hotfix
dotnet run scripts/ReleaseTool.cs -- prepare <next-codename> [next-version]
dotnet run scripts/ReleaseTool.cs -- prepare
dotnet run scripts/ReleaseTool.cs -- finalize
dotnet run scripts/ReleaseTool.cs -- publish
dotnet run scripts/ReleaseTool.cs -- self-test
```

Do not guess around a dirty worktree, wrong branch, conflicting draft, existing remote branch, or existing pull request. Report the exact state and stop if the tool cannot safely own the transition.

## Start a regular release draft

Use this when the user wants to begin the next regular release and no active draft exists.

1. Require a clean local `develop` branch. An explicitly supplied codename counts as approval of that codename; otherwise ask for one before invoking the tool. Let the tool allocate the version unless the user explicitly requests an override.
2. Tell the user that `start-next-release` will fast-forward from `origin/develop` and commit an empty draft scaffold.
3. Run `start-next-release` and report the allocated version and bootstrap commit.
4. If the user also asked to prepare release notes, continue by drafting them under the review gate. Otherwise stop with the empty draft ready for later work.

## Start a hotfix

Use this when urgent production work must branch from the current released `master`.

1. Require a clean, up-to-date local `master` and no conflicting draft in that branch's changelog state.
2. Tell the user that `start-hotfix` will fast-forward from `origin/master`, allocate the next hotfix suffix, create `hotfix/<version>`, and commit an empty draft scaffold.
3. Run `start-hotfix` and report the branch, version, and bootstrap commit.
4. Do not invent or implement the hotfix itself unless the user's request also includes that code change. When the fix is ready, draft release notes from the actual branch diff and use the review gate.

## Draft customer-facing release notes

Preserve existing human-written entries unless the user asks to revise them. Derive proposed additions from the actual shipped behavior in commits and diffs since `origin/master`; do not merely copy commit subjects.

Write notes in Russian, matching the existing changelog style:

- describe observable user value or corrected behavior;
- use complete, concise sentences with consistent punctuation;
- put features in `new`, meaningful refinements in `improved`, defects in `fixed`, and user-relevant protection changes in `security`;
- omit refactors, dependency bumps, tests, CI mechanics, internal architecture names, and implementation details unless they materially affect customers;
- avoid duplicates across categories and avoid claims not supported by the diff;
- do not expose secrets, exploit detail, personal data, or sensitive operational information.

Edit only the active draft file. Keep `date: null`; regular releases retain their codename and hotfixes omit it. Run `validate`, inspect the resulting diff, and then apply the hard review gate.

## Prepare after approval

Only proceed after the user has approved the exact final changelog and its dedicated notes commit exists.

For a regular release, obtain the next draft codename before preparation; use an explicit next version only when the user requests it. For a hotfix, pass no next-release arguments.

Before invoking `prepare`, state its material effects: it runs backend and frontend verification, creates or reuses the release/hotfix branch, dates and commits the approved changelog, pushes the branch, opens the pull request, and—for a regular release—commits the next empty draft on local `develop`. The user's post-review instruction to proceed must authorize these effects.

Run exactly one appropriate command:

```text
dotnet run scripts/ReleaseTool.cs -- prepare <next-codename> [next-version]
dotnet run scripts/ReleaseTool.cs -- prepare
```

If verification or a remote operation fails, inspect and report the tool's exact local/remote state. Do not retry blindly, delete a remote branch/tag, reuse a version, rewrite history, or manually reproduce the remaining steps.

On success, report the version, branch, notes commit, preparation commits, pull-request URL, and next regular draft when created. Do not merge the pull request unless the user separately asks.

## Finalize a merged release

Use `finalize` only after the release/hotfix pull request is confirmed merged and local `develop` is clean. Explain that it updates local `master`, merges released `master` back into local `develop`, cleans merged local release/hotfix branches, and updates still-active local release branches.

Run the tool once and report local branch state afterward. Do not push `develop`, resolve merge conflicts, delete unmerged hotfix branches, or rewrite history without separate user direction.

## Manual publish recovery

Normal publishing belongs to CI after a valid release/hotfix merge reaches `master`. Do not run `publish` as part of ordinary preparation or finalization.

Use `publish` only when the user explicitly requests recovery publishing. First confirm the current version, `release-kind`, merged source, expected tag/release state, and required credentials. Tell the user exactly which external image tags, Git tag, and GitHub Release may be created, then request final authorization immediately before the command. Make one attempt and report partial external state rather than deleting or overwriting published artifacts.

## Completion report

Report the release phase reached, current branch, version, draft/released state, commits created, pull request or publish result, verification outcome, and any intentional next action. A prepared pull request is not a finalized release, and a finalized local merge-back is not proof that CI publishing or deployment completed.
