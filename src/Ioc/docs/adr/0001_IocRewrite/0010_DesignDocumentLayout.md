---
status: accepted
date: 2026-11-23
---

# Design document layout

## Context and problem statement

Before the rewrite, the design of SourceGen.Ioc was in 37 specification files inside the generator project (`**/Spec/*.spec.md`). Those files were the source of truth for behavior. They did not record why a rule existed or which alternatives were rejected.

The rewrite (see [Rewrite of SourceGen.Ioc](README.md)) started from one guideline document that mixed principles, terms, limits, and detailed behavior. Seven specifications refined it. Each confirmed decision had to be written in two places, and one round of test results reached the specifications but not the guideline.

During the review, some decisions changed more than once. Several rejected options are likely to be proposed again by a later reader who does not know the background.

## Decision outcome

1. The design documents are in `src/Ioc/docs/`:
   - `Constitution.md` at the top level.
   - Specifications in `spec/`, with the suffix `.spec.md`.
   - Architectural Decision Records in `adr/`.
2. The constitution holds only what changes rarely and what a specification must not contradict: principles, scope and limits, terms, and the documented differences from MS.DI. Detailed behavior is in the specifications.
3. The specifications are the source of truth for current behavior. When behavior changes, the specification is rewritten in place. A specification does not mention the previous library.
4. An ADR records why a decision was made and which alternatives were rejected. It is not a place to look up current behavior.
5. An ADR is written only for a decision that has a rejected alternative someone can propose again, or that looks strange without its background. A decision that only fills in a detail stays in the specification.
6. An ADR follows the MADR template (<https://github.com/adr/madr>) where it fits. It has YAML front matter with `status` and `date`, then the sections "Context and problem statement", "Decision outcome", "Rejected options", and "Evidence". An author can add or leave out sections when the content needs it. One ADR covers one topic and can hold several related decisions.
7. The Evidence section lists the tested facts that the decision relies on, with tool and package versions. It must be understandable without the prototypes, because the prototypes are not in the repository. A fact that supports several ADRs is written once, in the ADR that relies on it most, and the other ADRs link to it.
8. ADRs that belong together are one set, in a folder `adr/NNNN_Set/`. `NNNN` is a four-digit sequence number of the set in the domain, and `Set` is the title of the set. The `README.md` of the folder gives the shared background and links to each ADR of the set. The ADRs of the set have file names of the form `NNNN_Title.md`: a four-digit sequence number in the set, starting at `0001`, and a title. Other documents refer to an ADR as `{Set}/{NNNN}`, for example `IocRewrite/0020`.
9. The `README.md` of a set has `status: open` while the work of the set goes on, and `status: closed` when it ends. In an open set, an ADR is edited in place when its decision changes, and the earlier decision moves to "Rejected options" with the reason. In a closed set, a later decision gets a new ADR. The `status` of the old ADR then changes to `superseded by` with a link, and its text stays.
10. When the constitution and a specification disagree, the constitution wins, and the person or agent who finds the conflict tells the user. A change to the constitution needs a new ADR.
11. All design documents are in English, in the style of the `ste80-humanizer` skill. Specifications also use the RFC 2119 keywords (MUST, MUST NOT, SHOULD, MAY) to mark how strict a rule is.
12. The old specification files are deleted in the same change that adds the new documents. Git history keeps them.
13. Prototypes and working notes are not added to the repository. A result that matters is copied into an ADR or a specification.

## Rejected options

- **Specifications as a sequence of ADRs.** A reader must read all related ADRs in order and merge them to learn the current behavior. One item in this review changed three times.
- **Specifications only, no ADRs.** Rejected options have no record, so they come back.
- **One ADR for each confirmed item.** The review confirmed about 70 items. That many records hide the important ones.
- **Keep detailed behavior in the top-level document.** Each rule then has two homes, and they drift apart.
- **Specifications that mention the previous library.** A specification states the current rule only. A comparison with the previous library belongs in an ADR.
- **The date in the file name of an ADR (`YYYYMMDDNN_Title.md`).** The ADRs of a long effort such as the rewrite are edited again while the work goes on, so the date of the file says little. A reference by date is also long and hard to read.
- **Always a new ADR for a changed decision, also in an open set.** During the rewrite, decisions change often. A chain of ADRs that replace parts of each other makes the reader merge them to learn the decision, which is the problem of "Specifications as a sequence of ADRs" above.
- **Traditional Chinese.** The review was done in Chinese. The project owner decided that the documents in the repository are in English.

## Evidence

No tests apply to this decision.
