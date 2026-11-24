---
name: sourcegen-spec-writing
description: "Use when: creating or updating SourceGen design documents: the constitution, specifications (`src/<Domain>/docs/spec/*.spec.md`), and Architectural Decision Records (`src/<Domain>/docs/adr/*.md`). Covers document roles and precedence, layout, RFC 2119 keyword usage, writing style, examples, and when an ADR is required."
argument-hint: "design document(s) to create or update and the behavior or decision to describe"
user-invocable: true
---

# SourceGen Spec Writing

## Documents and precedence

The design documents of a domain are in `src/<Domain>/docs/` (for IoC: `src/Ioc/docs/`).

| Document | Location | Role |
|---|---|---|
| Constitution | `docs/Constitution.md` | Principles, scope and limits, terms, documented differences. It changes rarely. |
| Specifications | `docs/spec/<Area>.spec.md` | The source of truth for current behavior. Rewrite in place when behavior changes. |
| Index | `docs/spec/SPEC.spec.md` | MUST link every specification. Update it when you add, rename, or remove one. |
| ADRs | `docs/adr/YYYYMMDDNN_Title.md` | Why a decision was made and which alternatives were rejected. Not a place to look up current behavior. |

Rules:

- Read the constitution before you write or change a specification.
- A specification MUST NOT contradict the constitution. If they disagree, the constitution wins, and you MUST tell the user about the conflict. Do not resolve it silently.
- A change to the constitution MUST come with a new ADR that gives the reason.
- Prototypes and working notes are not added to the repository. Copy a result that matters into an ADR or a specification.

## When to write an ADR

Write an ADR only when one of these is true:

- The decision has a rejected alternative that someone can propose again.
- The decision looks strange without its background.
- The decision changes the constitution.

A change that only fills in a detail stays in the specification.

ADR format:

- File name: `YYYYMMDDNN_Title.md`. The date, a two-digit sequence number for that date, and a title in PascalCase.
- Follow the MADR template (<https://github.com/adr/madr>) where it fits: YAML front matter with `status` and `date`, then `Context and problem statement`, `Decision outcome`, `Rejected options`, `Evidence`. Add or leave out sections when the content needs it.
- ADRs of the same date that belong together are one set. The first ADR of the set (`NN` = `01`) gives the shared background and links to the others. Add a new ADR of the set to that index.
- One ADR covers one topic and can hold several related decisions.
- `Evidence` lists each tested fact that the decision relies on, with the versions of the tools and packages used. It also says what was not tested. Write it so that a reader understands it without the prototype.
- Write a fact that supports several ADRs once, in the ADR that relies on it most. Link to it from the others.
- When a later decision replaces an ADR, change the `status` of the old ADR to `superseded by` with a link. Keep its text.

## Language and style

- All design documents are in English.
- Invoke the `ste80-humanizer` skill and write in that style: one idea per sentence, active voice, simple tenses, plain words, no semicolons, no dashes.
- Use one term for one thing. The terms are in the "Terms" section of the constitution. A term that belongs to one output only is defined in its specification.

## Specification structure

Follow the structure of neighboring specifications before you invent a new one. A specification starts with:

1. `# {Title}`
2. A note that names the parent document: `> Parent document: [Constitution](../Constitution.md). If this document and the constitution disagree, the constitution wins.`
3. The BCP 14 boilerplate (see below).

C# example blocks are split with `#region Define:`, `#region Generate:`, `#region Invalid:`, and `#region Usage:`, each closed with `#endregion`.

## Writing guidelines

- **Structure**: Markdown tables for rules and properties, Mermaid diagrams for data flow, code blocks with language tags, hierarchical headings.
- **Precision**: One unambiguous condition and action per rule. Include edge cases and defaults. Reference diagnostic IDs (for example `SGIOC001`) when you describe a constraint.
- **Examples**: At least one C# example per major feature, with valid and invalid usage. Keep examples minimal.
- **Consistency**: Reuse existing table structures. Do not restructure existing content unless asked.
- **No reference by number**: Do not refer to a part of another document by its number. The numbers change when an item or a section is added or removed. This applies to specifications, ADRs, and comments in code and tests.
  - An item of a numbered list or table: not "documented difference 21", "item 7", or "row 14". Describe the item in words, for example "a [documented difference](../Constitution.md#8-documented-differences-from-msdi)" or "the list of implicit rules".
  - A section: not "section 4.1" or "§8". Use the title of the section, for example "the section "Explicitness" of the constitution". A link can use the anchor that Markdown makes from the heading (`#41-explicitness`), but the link text uses the title.
  - Stable identifiers are fine: diagnostic IDs (`SGIOC001`), ADR numbers, and the steps of a list in the same document.
- **No history**: A specification states the current rule only. Do not mention a previous version of the library or how a rule changed. That belongs in an ADR.
- **Truthfulness**: Describe only behavior that exists or is explicitly planned. Never guess. If the intended behavior is unclear, stop and ask.

## Normative keywords (RFC 2119 / RFC 8174)

- Keywords (MUST, MUST NOT, SHOULD, SHOULD NOT, MAY, …) are normative **only in ALL CAPITALS**. Lowercase words keep their ordinary meaning.
- Use them in specifications and in the principles of the constitution. Do not use them in ADRs.
- Use them **sparingly**: only where required for correctness or to prevent harmful behavior, never to impose an arbitrary implementation choice.
- When you state MUST or SHOULD, explain the consequence of not following it.
- New specification files MUST include the BCP 14 boilerplate near the beginning.

Boilerplate statement, keyword definitions, and sources: [references/rfc2119.md](./references/rfc2119.md).
