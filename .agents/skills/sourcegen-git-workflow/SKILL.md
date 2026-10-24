---
name: sourcegen-git-workflow
description: "Use when: committing, naming branches, or opening/updating pull requests in the SourceGen repository. Covers Conventional Commit messages, branch naming, and the PR template."
argument-hint: "commit | branch | pr"
user-invocable: true
---

# SourceGen Git Workflow

## Branches

- `main` is always releasable; branch from `main` and merge back via pull request.
- Feature branches: `feature/<short-description>` (e.g. `feature/keyed-services`).
- Bug fix branches: `fix/<short-description>` (e.g. `fix/decorator-resolution`).

## Commit Messages

Use [Conventional Commits](https://www.conventionalcommits.org/): `<type>[(scope)]: <Summary>`

| Type | Use for |
|---|---|
| `feat` | New user-facing functionality |
| `fix` | Bug fixes |
| `refactor` | Code changes without behavior change |
| `perf` | Performance improvements |
| `test` | Test-only changes |
| `docs` | Documentation (`docs/`, specs, READMEs) |
| `chore` | Tooling, agent/skill instructions, housekeeping |
| `ci` | `.github/workflows/` changes |

- Optional scope, e.g. `chore(deps): Update nuget packages`.
- Imperative mood, summary line under 72 characters (e.g. `fix: Resolve decorator cycle`).
- Reference issues in the body or footer (`Fixes #42`).
- Append `!` after the type (or a `BREAKING CHANGE:` footer) for breaking changes.

## Pull Requests

1. One concern per PR.
2. Fill in every section of `.github/pull_request_template.md` (Description, Related Issue, Type of Change, Checklist, Testing); keep its headings and checklist.
3. Under **Testing**, list each exact test command you ran and its result.
4. All tests pass before requesting review; update docs and specs when behavior changes.

## Releases

Release tags (`ioc-v*`, `ioc-cli-v*`) trigger publish workflows — use the `sourcegen-release-commands` skill; never create release tags ad hoc.
