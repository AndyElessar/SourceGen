---
description: "Use when writing or reviewing SourceGen tests. Covers SourceGen test conventions and generator/analyzer snapshot testing; general TUnit APIs are in the `tunit` skill."
applyTo: "src/**/test/**/*.cs"
---

# SourceGen Test Rules

For TUnit APIs (assertions, data sources, hooks, parallelism, filters), use the `tunit` skill.

## Conventions

- Name tests `Subject_Scenario_ExpectedBehavior` (e.g. `Container_WithPropertyInjection_GeneratesPropertyAssignment`); copy the style of nearby tests.
- Do not emit `// Arrange`, `// Act`, `// Assert` comments.
- Cover both valid and invalid input, and feature flags on and off.

## Generator and Analyzer Tests

| Kind | Pattern |
|---|---|
| Generator snapshot | `SourceGeneratorTestHelper.RunGenerator<TGenerator>(source)` → `await result.VerifyCompilableAsync()` → `await Verify(SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs"))` |
| Expected compile errors | `await result.VerifyHasCompilationErrorsAsync()` |
| Analyzer | `await SourceGeneratorTestHelper.RunAnalyzerAsync<TAnalyzer>(source)` → assert on `SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC0XX")` |
| End-to-end / AOT | Real types in the TestCase project, exercised by the AOT test project |

## Snapshot Failures (Verify.TUnit)

- A failing snapshot writes `*.received.txt` next to `*.verified.txt`; diff the two.
- Accept the new output (replace `.verified.txt` with `.received.txt`) **only** when the change is intended by the spec or request; otherwise fix the generator.
- Never edit `*.verified.txt` just to make a test pass. Delete leftover `*.received.txt` files.
