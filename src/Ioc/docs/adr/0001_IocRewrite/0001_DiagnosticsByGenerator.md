---
status: accepted
date: 2026-11-23
---

# Diagnostics by the generator

## Context and problem statement

The previous design had two analyzers (`RegisterAnalyzer`, `ContainerAnalyzer`) next to the generator. Each analyzer repeated the analysis of the generator to find IoC semantic errors. The two implementations disagreed many times: a long series of issues was about an analyzer and the generator that resolved the same dependency differently.

Roslyn guidance says that a generator should not report diagnostics, because a `Diagnostic` or `Location` in a pipeline model breaks incremental caching.

The previous diagnostics used the IDs `SGIOC001` to `SGIOC046`. Some were warnings that only said "this registration was ignored".

## Decision outcome

1. The generator reports all IoC semantic diagnostics, from the same analysis result that it uses to generate code. No separate analyzer repeats that analysis. An analyzer is acceptable only for an advisory check that does not affect generation.
2. The generator uses two pipelines:
   - The generation pipeline carries only equatable models. It carries no `Diagnostic`, `Location`, or `SyntaxTree`.
   - The diagnostic pipeline carries equatable diagnostic data: ID, message arguments, file path, and span. At the output step it combines with `CompilationProvider`, finds the `SyntaxTree` by file path, creates the `Location` from that tree, and reports.
3. There are three levels:
   - **Error.** The descriptor has `WellKnownDiagnosticTags.NotConfigurable`. The user cannot disable or lower it.
   - **Warning.** For a situation that can be a problem but is not always wrong. The descriptor has no `NotConfigurable` tag, so the user can change the level. The generator does not read the changed level. The generated code is the same in all cases.
   - **Info.** For a fact that the user can want to know. It has no `NotConfigurable` tag.
4. Each diagnostic has exactly one consequence: skip the declaration, do not generate the output, or inform only.
5. All previous diagnostics are removed. The new IDs are grouped by category and start at `SGIOC0xx`. The new numbering does not avoid the old numbers, so one ID can mean different things in old and new versions.

## Rejected options

- **Separate analyzers.** Two implementations of one analysis do not stay equal.
- **A `Location` in the pipeline model.** It makes every item of a file run again on any edit of that file.
- **A `Location` created from a path and a span only.** Such a location ignores `#pragma`, `[SuppressMessage]`, and `.editorconfig`.
- **Errors that the user can disable.** A disabled error lets a build succeed with wrong or missing generated code.
- **Keep the old IDs, or start the new IDs after the old range.** The old diagnostics describe rules that no longer exist. Avoiding the old range leaves `SGIOC0xx` unused for no benefit.
- **Only Error and Info.** "A singleton holds a transient service" is often harmless, so Error blocks valid code. Info does not appear in `dotnet build` output, so it warns nobody.

## Evidence

Tested with .NET SDK 11.0.100 and Roslyn 5.12.0 on Windows x64.

- A location created with `Location.Create(path, span, lineSpan)` does not react to `#pragma`, `[SuppressMessage]`, or a file section of `.editorconfig`. It reacts only to `NoWarn`, a global config, and `TreatWarningsAsErrors`.
- A location created with `Location.Create(tree, span)`, where the tree comes from the compilation, reacts correctly to every suppression and configuration method.
- With the two pipelines, the generation pipeline does not run again when code moves to another position. The diagnostic pipeline runs again on each compilation change, but only its last combine and output steps.
- With a `Location` in the model, any edit of the file that holds the target makes all items of that file run again, also when positions do not change.
- An Error with `NotConfigurable` cannot be disabled or lowered by `#pragma`, `NoWarn`, `.editorconfig`, a ruleset, or a global config. Without the tag, six of those methods disable the error and the build succeeds.
- Known effect that cannot be avoided: when the user applies `[SuppressMessage]` to an Error with `NotConfigurable`, the build still fails, but the error message is hidden. Only MSB3030 (file to copy not found) appears.
- An Info diagnostic does not appear in the console output of `dotnet build`. It appears in the IDE and in the SARIF log. An Info diagnostic with `NotConfigurable` cannot be suppressed by `#pragma`.
- A Warning from a generator, at a location on a `SyntaxTree`:
  - `#pragma warning disable` and `[SuppressMessage]` suppress it.
  - `NoWarn` and `.editorconfig` with `none` or `silent` remove it.
  - `.editorconfig` with `suggestion` lowers it, and `error` raises it and fails the build.
  - `TreatWarningsAsErrors` raises it. `WarningsNotAsErrors` keeps it a warning.
  - In every case where the build passed, the generated code was present and usable.
- `dotnet_analyzer_diagnostic.severity = none`, a setting for all analyzers at once, does not affect a generator diagnostic.

Not tested: the behavior of a `Diagnostic` or `SyntaxNode` (as opposed to a `Location`) in the model.
