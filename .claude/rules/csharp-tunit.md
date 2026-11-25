---
paths:
  - "src/**/test/**/*.cs"
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
| Generator snapshot | `SourceGeneratorTestHelper.RunGenerator<TGenerator>(source)` → `await result.VerifyCompilableAsync()` → `await Verify(SnapshotHelper.MembersByPrefix(result.GetGeneratedSource("Container.g.cs")!, [names], prefixes))`. Generated member names end with a hash, so select them by prefix |
| Expected compile errors | `await result.VerifyHasCompilationErrorsAsync()` |
| Diagnostics | Mark each expected diagnostic in the source (`{\|SGIOC002:Foo\|}`), then `DiagnosticMarkup.Parse(sources)` → `RunGenerator` → `await result.VerifyDiagnosticsAsync(expected)`. Assert on the message with `result.ReportedDiagnostics`. The generator reports all diagnostics; there is no separate analyzer |
| Determinism | `await SourceGeneratorTestHelper.VerifyDeterministicAsync<TGenerator>(sources)` |
| End-to-end / AOT | Real types in the AOT test project, published with NativeAOT and run |

## Snapshot Failures (Verify.TUnit)

- A failing snapshot writes `*.received.txt` next to `*.verified.txt`; diff the two.
- Accept the new output (replace `.verified.txt` with `.received.txt`) **only** when the change is intended by the spec or request; otherwise fix the generator.
- Never edit `*.verified.txt` just to make a test pass. Delete leftover `*.received.txt` files.
