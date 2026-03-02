---
description: "Use when making commits, creating branches, or submitting PRs. Covers commit conventions, branch strategy, and release tagging."
---

# Git Workflow

## Branch Strategy

- **Main branch:** `main` — always deployable
- **Feature branches:** `feature/<short-description>` (e.g., `feature/keyed-services`)
- **Bug fix branches:** `fix/<short-description>` (e.g., `fix/decorator-resolution`)
- Create branches from `main`; merge back via Pull Request

## Commit Conventions

- Write clear, descriptive commit messages
- Use imperative mood: "Add feature" not "Added feature"
- Keep the first line under 72 characters
- Reference issue numbers when applicable: `Fix #42: resolve decorator cycle`

## Pull Request Expectations

1. One concern per PR — keep changes focused
2. Include a description of what changed and why
3. Ensure all tests pass before requesting review
4. Update documentation if behavior changes
5. Follow the PR template if one exists in `.github/PULL_REQUEST_TEMPLATE/`

## Release Tagging

Uses [nbgv](https://github.com/dotnet/Nerdbank.GitVersioning) for versioning with project-specific tag prefixes:

| Project | Tag Prefix | Example Tag |
|---------|-----------|-------------|
| SourceGen.Ioc | `ioc-v` | `ioc-v0.9.1-alpha` |
| SourceGen.Ioc.Cli | `ioc-cli-v` | `ioc-cli-v1.0.0` |

Tags trigger publish workflows. See the [release commands skill](../skills/sourcegen-release-commands/SKILL.md) for detailed release steps.
