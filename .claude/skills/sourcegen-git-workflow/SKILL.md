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

### Generated sample code

When a generator change regenerates `samples/**/Generated/*.g.cs`, commit the regenerated files in the **same** commit as the generator change, not in a separate `chore` commit.

- Build the samples at the commit being made, so the generated code matches it.
- Do not `git restore` sample diffs as build noise. The generator stamps only `major.minor.patch` into `[GeneratedCode]`, so a version-only diff appears only when `version.json` changes. A version with a height suffix means the samples were built with a stale generator.

## Pull Requests

1. One concern per PR.
2. Fill in every section of `.github/pull_request_template.md` (Description, Related Issue, Type of Change, Checklist, Testing); keep its headings and checklist.
3. Under **Testing**, list each exact test command you ran and its result.
4. All tests pass before requesting review; update docs and specs when behavior changes.

### Checks and merging

- CI (build, AOT tests, CodeQL, generator and NativeAOT benchmarks) runs on PRs, including stacked PRs, and reruns when a PR is retargeted. Benchmarks take about 25 minutes.
- Benchmark timings on shared runners swing by 5–20% with unchanged allocations. Judge a change by allocations; the benchmark check fails on allocation increases (above 5%, 10% for `AnalyzerBenchmarks`) and only reports time changes.
- CI does not rerun when `main` moves. If `main` changed since the last run in a way that can interact with the PR, merge `main` into the branch to rerun CI before merging.
- Merge with a merge commit: `gh pr merge <n> --merge`.
- A PR in a GitHub stack (`gh stack link <bottom-pr> ... <top-pr>`) cannot be merged with `gh pr merge`; use `gh stack merge <pr> --yes --merge`, which merges up to that PR one at a time.

## Releases

Release tags (`ioc-v*`, `ioc-cli-v*`) trigger publish workflows — use the `sourcegen-release-commands` skill; never create release tags ad hoc.
