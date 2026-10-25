---
name: sourcegen-release-commands
description: "Use when: publishing SourceGen.Ioc or SourceGen.Ioc.Cli with nbgv set-version, git tags, and tag-triggered GitHub Actions publish workflows."
argument-hint: "project=<ioc|ioc-cli> releaseType=<prerelease|release> version=<semver>"
user-invocable: true
---

# SourceGen Release Commands

Use this skill to run repeatable PowerShell release commands for SourceGen packages.

## Supported Projects

| Project key | csproj | version.json | test project | tag prefix | workflow |
|---|---|---|---|---|---|
| `ioc` | `src/Ioc/src/SourceGen.Ioc/SourceGen.Ioc.csproj` | `src/Ioc/src/SourceGen.Ioc/version.json` | `src/Ioc/test/SourceGen.Ioc.Test/SourceGen.Ioc.Test.csproj` | `ioc-v` | `ioc.publish.yml` |
| `ioc-cli` | `src/Ioc/src/SourceGen.Ioc.Cli/SourceGen.Ioc.Cli.csproj` | `src/Ioc/src/SourceGen.Ioc.Cli/version.json` | `src/Ioc/test/SourceGen.Ioc.Cli.Test/SourceGen.Ioc.Cli.Test.csproj` | `ioc-cli-v` | `ioc.cli.publish.yml` |

## Inputs

- `project`: `ioc` or `ioc-cli`
- `releaseType`: `prerelease` or `release`
- `version`: full SemVer

Version examples:
- prerelease: `0.9.2-alpha`
- stable release: `0.9.2`

## Scripts

- [release script](./scripts/release.ps1): set version with `nbgv set-version`, run tests, commit, tag, and push.
- [monitor release script](./scripts/monitor-release.ps1): list publish workflow runs and optionally publish a draft GitHub release.
- [post-release script](./scripts/post-release.ps1): run `nbgv prepare-release`, commit next iteration, and push.

## Usage

### Pre-release

```powershell
./.agents/skills/sourcegen-release-commands/scripts/release.ps1 -ProjectKey ioc -ReleaseType prerelease -Version 0.9.2-alpha
```

### Stable Release

```powershell
./.agents/skills/sourcegen-release-commands/scripts/release.ps1 -ProjectKey ioc -ReleaseType release -Version 0.9.2
```

### SourceGen.Ioc.Cli Pre-release

```powershell
./.agents/skills/sourcegen-release-commands/scripts/release.ps1 -ProjectKey ioc-cli -ReleaseType prerelease -Version 1.0.1-alpha
```

### Monitor Publish Workflow

```powershell
./.agents/skills/sourcegen-release-commands/scripts/monitor-release.ps1 -ProjectKey ioc
```

### Publish Draft GitHub Release

```powershell
./.agents/skills/sourcegen-release-commands/scripts/monitor-release.ps1 -ProjectKey ioc -Tag ioc-v0.9.2-alpha -PublishDraft
```

### Post-release (Next Iteration)

```powershell
./.agents/skills/sourcegen-release-commands/scripts/post-release.ps1 -ProjectKey ioc
```

## Versioning Rules (nbgv)

- Each package has its own `version.json` next to its `.csproj`, versioned with [nbgv](https://github.com/dotnet/Nerdbank.GitVersioning).
- Tags use the project prefix plus the full `NuGetPackageVersion`, including prerelease labels (`ioc-v0.9.2-alpha`, `ioc-cli-v1.0.1-alpha`). Tags trigger the publish workflows.
- `nbgv tag` only creates `v{version}` tags, so create project-prefixed tags with `git tag -a` (the release script does this).
- `publicReleaseRefSpec` in `version.json` MUST match the project tag prefix and the intended SemVer shape (stable + prerelease); otherwise nbgv appends unexpected suffixes to `NuGetPackageVersion`.
