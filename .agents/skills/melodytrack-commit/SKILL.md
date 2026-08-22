---
name: melodytrack-commit
description: Create one or more focused local MelodyTrack Git commits from an explicitly authorized, successfully verified change batch. Use when the user explicitly asks to commit current repository changes; do not use for push, pull request, history rewriting, release, deployment, or indiscriminate staging.
---

# MelodyTrack Commit

Commit an authorized MelodyTrack change batch locally. This workflow may stage files and create new commits only. It never authorizes push, pull requests, releases, deployments, amend, rebase, reset, or other history rewriting.

## Inspect before mutating Git

1. Work from the repository root and read [the repository guidance](../../../AGENTS.md) plus scoped guidance for every file under consideration.
2. Inspect `git status --short --branch`, staged and unstaged diffs, untracked files, and recent commit subjects.
3. Identify which files belong to the user's current change batch. Preserve pre-existing, unrelated, or ambiguous changes. Treat an untracked `.codex/config.toml` as user-local unless the task explicitly includes it.
4. Exclude secrets, credentials, binlogs, `TestResults`, coverage output, dependency caches, build output, editor files, and other transient artifacts.

If ownership of a material change cannot be established from the task and current conversation, stop before staging it and ask for direction. Never make an unrelated working tree clean merely for convenience.

## Require verification of the exact tree

Use [$melodytrack-verify](../melodytrack-verify/SKILL.md) before staging unless the current conversation already contains a successful verification result for the exact current file contents after the latest mutation.

- Any product, generated-file, formatter, or instruction change after verification invalidates that result and requires verification again.
- If a required lane fails or is blocked, do not create a commit. Report the failure and leave the working tree intact.
- Review every mutation produced by verification before deciding that it belongs in the change batch.
- A verification result proves commit readiness only. It does not imply release readiness.

## Plan logical commits

Split the batch by independently understandable behavior or infrastructure boundary. Each commit must remain coherent and bisectable:

- keep an implementation with the tests and generated output that prove that implementation;
- keep required build/configuration wiring with the feature that depends on it unless it is independently useful;
- separate unrelated documentation, tooling, or workflow changes;
- do not split solely by file type or force an arbitrary number of commits.

Use short imperative subjects that describe the concrete change. Do not mention roadmap stages, phases, milestones, verification status, or generated tooling. Keep commit messages and repository content professional.

## Stage and commit safely

Before the first command that writes the Git index or refs, request elevated filesystem permission for the repository `.git` directory when the environment requires it. Do not first attempt the mutation in a restricted sandbox.

For each planned commit:

1. Stage only explicit paths with `git add -- <paths>`. Never use `git add -A`, `git add .`, `git commit -a`, or a wildcard broad enough to capture unrelated files.
2. Inspect `git diff --cached --check`, `git diff --cached --stat`, and the complete staged diff.
3. Confirm that the staged diff matches one planned commit and contains no transient artifacts, secrets, unrelated edits, or verifier surprises.
4. Create a new commit with the approved clean subject.
5. Inspect the created commit and the remaining working tree before proceeding to another logical commit.

If a commit command fails, diagnose the exact error without resetting, unstaging, or rewriting history unless the user explicitly authorizes that recovery action.

## Report the result

Report every created commit as `<short-hash> <subject>`, summarize any intentionally uncommitted files, and show the final branch/status state. Stop there: never push or start release tooling from this workflow.
