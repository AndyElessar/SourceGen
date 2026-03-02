---
description: "Repository directory structure with read/write annotations for agent boundary enforcement."
---

# Project Structure

## Directory Layout

| Directory | Purpose | Default Access |
|-----------|---------|----------------|
| `.github/agents/` | Agent definition files | ⚠️ Ask first |
| `.github/instructions/` | Shared instruction files for agents and Copilot | ⚠️ Ask first |
| `.github/skills/` | Copilot custom skills with scripts | ⚠️ Ask first |
| `.github/workflows/` | GitHub Actions CI/CD workflows | ⚠️ Ask first (DevOps agent) |
| `docs/` | User-facing documentation (VitePress) | ✏️ Write (Doc agent) |
| `samples/` | Sample projects for end users | 📖 Read-only |
| `src/**/Spec/` | Specification documents | ✏️ Write (Spec agent) |
| `src/**` (except `**/Spec/`) | Source code, tests, benchmarks | ✏️ Write (Implement agent) |
| `artifacts/` | Build/pack outputs | 🔧 Generated (not committed) |
| `assets/` | Repository assets (icon, images) | 📖 Read-only |

## Agent Access Summary

| Agent | Primary Write Scope | Read Scope |
|-------|---------------------|------------|
| Orchestrator | None (delegates only) | Everything |
| Explore | None (read-only) | Everything |
| Implement | `src/**` (except `**/Spec/`) | Everything |
| Review | None (read-only) | Everything |
| Spec | `**/Spec/*.md` | Everything |
| Doc | `docs/` | Everything |
| DocReview | None (read-only) | Everything |
| DevOps | `.github/workflows/` | Everything |
