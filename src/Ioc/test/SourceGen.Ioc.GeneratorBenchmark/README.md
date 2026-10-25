# IoC generator benchmarks

Measures the Roslyn generator and analyzers, not the generated container's runtime performance.
This separate project keeps Roslyn dependencies out of the Native AOT runtime benchmarks.

## Scenarios

| Scenario | Input for `Count = N` | Edit |
| --- | --- | --- |
| DependencyChain | N registered services forming a constructor dependency chain | Unrelated method body |
| Wrappers | N service/keyed-service/consumer triples using Func and Dictionary, plus one container | Unrelated method body |
| DuplicateInvocation | N services and one open generic registration | Add a duplicate closed-generic service lookup |
| DisabledContainer | Same triples and container as Wrappers, with only Register output enabled | Unrelated method body |
| RegisterFor | N assembly-level registration attributes | Unrelated method body |

- `Cold` creates a fresh compilation and driver for every operation. Parsing source text and loading metadata references are excluded.
- `Edit` always starts from the same previously run driver and applies the edited compilation. The returned driver is not reused, so this does not accidentally measure a no-op cached run.
- Setup validates the compilation and generated output that each benchmark measures (initial for `Cold`, edited for `Edit`) outside the timed operation. The edited compilation's semantic caches are warmed during `Edit` setup.
- MemoryDiagnoser reports managed allocations per operation, not retained heap size.
- These synthetic workloads isolate generator costs; they do not measure total build or IDE latency.

## Analyzer benchmarks

`AnalyzerBenchmarks` runs `RegisterAnalyzer` and `ContainerAnalyzer` separately on the generator output of the `DependencyChain`, `Wrappers` and `RegisterFor` scenarios, using the same `Count` values.

| Benchmark | Analyzer |
| --- | --- |
| AnalyzerOverhead | A no-op analyzer that registers the same action kinds (symbol, attribute syntax, semantic model, compilation end) with empty callbacks |
| RegisterAnalyzer | `RegisterAnalyzer` |
| ContainerAnalyzer | `ContainerAnalyzer` |

- Each operation calls `CompilationWithAnalyzers.GetAnalyzerDiagnosticsAsync` on the same compilation. Roslyn analyzes a copy of it, so every result includes the binding that the analyzer actions trigger. Subtract `AnalyzerOverhead` to estimate the analyzer's own cost.
- Analysis is sequential (`concurrentAnalysis: false`) so results do not depend on the runner's core count.
- Generator output is created and validated once in setup. Setup also fails if an analyzer throws or reports an error.

## Reproduce a comparison

Run from the repository root. Do not run builds or tests concurrently with measurements.
Use the same machine, SDK, runtime, environment and benchmark arguments for both versions.

```powershell
dotnet run --project src\Ioc\test\SourceGen.Ioc.GeneratorBenchmark\SourceGen.Ioc.GeneratorBenchmark.csproj -c Release -- --filter '*Count: 500*' --job short --warmupCount 3 --iterationCount 8 --iterationTime 300 --envVars DOTNET_TieredCompilation:0 --exporters json --artifacts "$env:TEMP\SourceGen-Ioc-GeneratorBenchmark-before"
```

Repeat after changing the generator, replacing `before` with `after`. `--envVars` disables tiered compilation only in the measured benchmark processes, which avoids JIT tier transitions during short measurements; it is an experimental control, not a recommended production setting. Use `--filter '*'` to include the smaller 100-unit workloads.

Compare the two runs:

```powershell
dotnet run --project src\Ioc\test\SourceGen.Ioc.GeneratorBenchmark\SourceGen.Ioc.GeneratorBenchmark.csproj -c Release -- --compare "$env:TEMP\SourceGen-Ioc-GeneratorBenchmark-before\results" "$env:TEMP\SourceGen-Ioc-GeneratorBenchmark-after\results" "$env:TEMP\generator-comparison.md" 5
```

The comparison mode reads BenchmarkDotNet JSON reports, a directory containing them, or a baseline file, and writes a Markdown table. A baseline path that does not exist reports the second run without a baseline. It exits with code 1 when allocated bytes increase by more than the threshold percentage, or a benchmark in the second run has no result, failed, or has no allocation data. A baseline entry without allocation data also fails unless the updated baseline below supplies it. A time change above the threshold is reported only when the 99.9% confidence intervals do not overlap. Also review the errors and standard deviations in the CSV reports rather than only the means.

An optional fifth argument names an updated baseline file. Allocation increases within the threshold of that file, and benchmarks it no longer contains, are reported as accepted instead of failing.

Write a run as a baseline file, recording where it was measured:

```powershell
dotnet run --project src\Ioc\test\SourceGen.Ioc.GeneratorBenchmark\SourceGen.Ioc.GeneratorBenchmark.csproj -c Release -- --export-baseline "$env:TEMP\SourceGen-Ioc-GeneratorBenchmark-after\results" "$env:TEMP\baseline.json" "local run"
```

The file keeps the host, the mean with its 99.9% confidence interval, and the allocated bytes of each benchmark, sorted and rounded so that it diffs cleanly. The export fails without writing a file when any benchmark failed or has no allocation data, so a baseline is always complete.

## Verify output equivalence

The verification mode compiles all scenarios at both sizes, checks for errors, and records each generated file's SHA-256 hash, keyed by scenario, size, edit state and hint name. It also records a hash of each analyzer's sorted diagnostics on the initial generator output, keyed by scenario, size and analyzer.

```powershell
dotnet run --project src\Ioc\test\SourceGen.Ioc.GeneratorBenchmark\SourceGen.Ioc.GeneratorBenchmark.csproj -c Release -- --verify "$env:TEMP\generator-before.json"
# Repeat on the modified version with generator-after.json.
Compare-Object (Get-Content "$env:TEMP\generator-before.json") (Get-Content "$env:TEMP\generator-after.json")
```

No differences are expected for behavior-preserving optimizations. GeneratorPerformanceTests also uses the same input builders to verify incremental cache behavior and that the analyzer scenarios are valid, without timing-sensitive assertions.

## Continuous integration

[ioc.benchmark.generator.yml](../../../../.github/workflows/ioc.benchmark.generator.yml) runs the `Count = 500` generator and analyzer workloads with the arguments above for pull requests to `main` and pushes to `main` that change the generator, its runtime library, this harness, shared build files, the baseline or benchmark workflow/comment code. Changes to Markdown files (`*.md`), such as specs and this README, do not trigger it. It can also be started manually.

The baseline is [`benchmarks/baseline.json`](../../../../benchmarks/baseline.json) on `main`.

| Event | Baseline |
| --- | --- |
| Pull request | The file in the `main` commit that the pull request merge commit is based on, so a pull request cannot change its own baseline |
| Push to `main`, manual run | The file in the measured commit |

- The baseline was measured by an earlier run, possibly on a different runner machine. The comparison warns when the hosts differ. Time changes only produce warnings because shared runners are noisy.
- The job fails when allocated bytes increase by more than 5%, or a head result is missing, failed or has no allocation data.
  - An allocation regression or a missing head result still uploads the `baseline.json` artifact, so an intentional change can be accepted by committing it as described in [Update the baseline](#update-the-baseline).
  - A failed benchmark or missing allocation data makes the export refuse to write `baseline.json`, so no artifact is uploaded. The comparison still lists every benchmark.
- When the baseline file does not exist, the head results are reported without a baseline. A benchmark that the baseline does not contain yet, such as a newly added one, is reported as "No baseline" and does not fail the job.
- The comparison is written to the job summary. The run's results are uploaded as an unzipped `baseline.json` artifact and all BenchmarkDotNet output as the `ioc-generator-benchmark-<run id>` artifact, both for 7 days.
- For pull requests, [ioc.benchmark.generator.comment.yml](../../../../.github/workflows/ioc.benchmark.generator.comment.yml) creates or updates a single comment with the comparison. It runs from the default branch through `workflow_run`, so pull request code never receives a write token.

### Update the baseline

The workflow never writes to the repository. To accept new results, for example an intentional allocation increase or an improvement that later changes should be measured against:

1. Download `baseline.json` from the pull request's run. The comparison links to it.
2. Commit it as `benchmarks/baseline.json` in the same pull request.

The next run still compares with the baseline on `main`, and passes the updated file as the accepted baseline. Allocation increases within 5% of the updated file, and benchmarks that it removes, are reported as accepted instead of failing, and reviewers see the baseline change in the pull request diff. After the merge, pushes to `main` compare with the new file.
