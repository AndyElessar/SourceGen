---
name: sourcegen-ci-workflows
description: "Use when: creating or modifying SourceGen GitHub Actions workflows (`.github/workflows/`) — CI build/test, pack, NuGet Trusted Publishing (OIDC), GitHub releases, or nbgv versioning configuration."
argument-hint: "workflow change: add workflow, fix pipeline, update publish config, etc."
user-invocable: true
---

# SourceGen CI Workflows

To run an actual release (set version, tag, push, monitor), use the `sourcegen-release-commands` skill instead.

## Workflows

| Workflow | Type | Trigger |
|---|---|---|
| `ioc.ci.yml`, `ioc.cli.ci.yml` | CI | push to `main` + `paths` filters (specs excluded), `workflow_dispatch` |
| `ioc.publish.yml`, `ioc.cli.publish.yml` | Publish | tags `ioc-v*`, `ioc-cli-v*` |
| `docs.yml` | Docs site (GitHub Pages) | push to `main` + `paths: docs/**`, `workflow_dispatch` |
| `ioc.benchmark.nativeaot.yml` | Native AOT benchmark | tags `ioc-v*`, `workflow_dispatch` |
| `ioc.benchmark.generator.yml` | Generator benchmark: compares head with the committed `benchmarks/baseline.json` from `main`; fails on >5% allocation regression unless the PR updates the baseline | `pull_request` + push to `main` with `paths` filters, `workflow_dispatch` |
| `ioc.benchmark.generator.comment.yml` | Posts the generator benchmark report as a pull request comment | `workflow_run` of the generator benchmark |

Read the existing workflow (and a sibling of the same type) before editing; mirror its structure.

## Key Configuration

| Setting | Value |
|---|---|
| Runner | `ubuntu-latest` |
| .NET SDK | Build jobs: `actions/setup-dotnet` with `global-json-file: global.json` (.NET 11 RC2) and `dotnet-version: '10.0.x'` for the .NET 10 test runtime; publish-only jobs use .NET 10 |
| Workflow env | `DOTNET_NOLOGO`, `DOTNET_SKIP_FIRST_TIME_EXPERIENCE`, `DOTNET_CLI_TELEMETRY_OPTOUT` = `true` |
| Artifact retention | `retention-days: 7` |
| Tests | `dotnet run --project <test.csproj> -c Release --no-build` (TUnit) |

## Rules

- **Triggers**: CI workflows use `paths`; publish workflows use project tag prefixes.
- **Action versions**: pin an explicit stable **major** tag (e.g. `@v7`) and match the major already used elsewhere in the repo; never `@latest`, a branch ref, or mixed majors for the same action.
- **Publish shape**: three jobs `build -> publish -> release`.
  - `build`: restore, build `-c Release`, run tests, pack to `./artifacts`, upload artifact.
  - `publish`: `needs: build`, `if: startsWith(github.ref, 'refs/tags/')`, `permissions: id-token: write`, `environment: nuget-publish`, `NuGet/login@v1` with `user: ${{ secrets.NUGET_USER }}`, then `dotnet nuget push ... --skip-duplicate` using the login step's temporary key.
  - `release`: `needs: publish`, `contents: write`, `gh release create` from the downloaded artifacts.
- **Permissions**: least privilege per job (`contents: read` by default).

## Security Red Lines

- Use NuGet [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) (OIDC) only — never long-lived API keys. `NUGET_USER` is the nuget.org profile name, not a key.
- Never remove the tag-based publish gate, `id-token: write`, `NuGet/login@v1`, or `environment: nuget-publish` from publish jobs.
- Never use `pull_request_target`. Pull request feedback that needs a write token goes through a separate `workflow_run` workflow that only reads artifacts from the pull request run as data, validates them, and requires the pull request head SHA and repository to match the triggering run.

## Changes That Need User Confirmation

New workflows; trigger, tag, or branch changes; environment, runner, or SDK changes; `NUGET_USER` changes.

## Versioning

Tag prefixes, `version.json`, and `publicReleaseRefSpec` rules live in the `sourcegen-release-commands` skill.

## Verification Checklist

- [ ] YAML parses; path filters and tag patterns are correct
- [ ] Action versions pinned to stable majors consistent with the repo
- [ ] Publish job: OIDC, `id-token: write`, `NuGet/login@v1`, `environment: nuget-publish`, `--skip-duplicate`
- [ ] Job dependencies (`needs`) and per-job permissions are correct
- [ ] Manual follow-ups listed (nuget.org Trusted Publisher, GitHub environment, secrets, tags)
