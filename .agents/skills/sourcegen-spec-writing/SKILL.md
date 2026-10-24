---
name: sourcegen-spec-writing
description: "Use when: creating or updating SourceGen specification documents (`**/Spec/*.spec.md`). Covers spec layout, structure, RFC 2119 keyword usage, examples, and diagnostic references for both human developers and AI agents."
argument-hint: "spec file(s) to create or update and the behavior to describe"
user-invocable: true
---

# SourceGen Spec Writing

Specs describe functionality and implementation requirements. They serve **two audiences**:

- **Human developers** — prose, examples, diagrams.
- **AI agents** — structured tables, precise rules, acceptance criteria.

## Location and Index

| Spec kind | Location |
|---|---|
| Generator feature specs | `src/<Domain>/src/<Generator>/Spec/<Area>.<Feature>.spec.md` (e.g. `Register.Tags.spec.md`, `Container.Lifetime.spec.md`) |
| Analyzer / diagnostic specs | `src/<Domain>/src/<Generator>/Analyzer/Spec/` |
| Index | `Spec/SPEC.spec.md` in each folder — MUST link every sub-spec; update it when adding, renaming, or removing a spec |

Domain `AGENTS.md` files (e.g. `src/Ioc/AGENTS.md`) list the spec entry points for each domain.

## Document Structure

Follow the structure of neighboring specs before inventing a new one. Feature specs typically use:

1. `# {Feature Title}`
2. `## Overview` — what the feature does and why
3. `## Feature: {Name}` — rules as bullet lists or tables, with `###` subsections such as *Registration Logic* and *Generated Code Pattern*
4. C# example blocks split with `#region Define:`, `#region Generate:`, and `#region Usage:` (each closed with `#endregion`)
5. `## See Also` — links to related specs

## Writing Guidelines

- **Structure**: Markdown tables for rules/properties, Mermaid diagrams for data flow, code blocks with language tags, hierarchical headings.
- **Precision**: One unambiguous condition + action per rule. Include edge cases and defaults. Reference diagnostic IDs (e.g. `SGIOC001`) when describing constraints.
- **Examples**: At least one C# example per major feature, showing valid **and** invalid usage. Keep examples minimal.
- **Consistency**: Reuse existing table structures; place new sections in logical order; do not restructure existing content unless asked.
- **Truthfulness**: Describe only behavior that exists or is explicitly planned. Never guess — if intended behavior is unclear, stop and ask.

## Normative Keywords (RFC 2119 / RFC 8174)

- Keywords (MUST, MUST NOT, SHOULD, SHOULD NOT, MAY, …) are normative **only in ALL CAPITALS**; lowercase words keep their ordinary meaning.
- Use them **sparingly** — only where required for correctness or to prevent harmful behavior, never to impose an arbitrary implementation choice.
- When stating MUST or SHOULD, explain the consequence of not following it.
- New spec files MUST include the BCP 14 boilerplate near the beginning. When editing an existing spec that lacks it, do not retrofit it unless the task asks for it.

Boilerplate statement, keyword definitions, and sources: [references/rfc2119.md](./references/rfc2119.md).
