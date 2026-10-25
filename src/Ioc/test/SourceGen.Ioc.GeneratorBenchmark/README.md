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

`AnalyzerBenchmarks` runs `RegisterAnalyzer` and `ContainerAnalyzer` separately on the source of the `DependencyChain`, `Wrappers` and `RegisterFor` scenarios, using the same `Count` values.

| Benchmark | Analyzer |
| --- | --- |
| AnalyzerOverhead | A no-op analyzer that registers the same action kinds (symbol, attribute syntax, semantic model, compilation end) with empty callbacks |
| RegisterAnalyzer | `RegisterAnalyzer` |
| ContainerAnalyzer | `ContainerAnalyzer` |

- Each operation calls `CompilationWithAnalyzers.GetAnalyzerDiagnosticsAsync` on the same compilation. Roslyn analyzes a copy of it, so every result includes the binding that the analyzer actions trigger. Subtract `AnalyzerOverhead` to estimate the analyzer's own cost.
- Analysis is sequential (`concurrentAnalysis: false`) so results do not depend on the runner's core count.
- The analyzed compilation does not include generator output. Analysis binds every syntax tree, and binding the generated container of the `Wrappers` scenario at `Count = 500` takes over a minute per operation, which would hide the analyzer's own cost and exceed the CI timeout.
- Setup fails if an analyzer throws or reports an error.

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

The comparison mode reads BenchmarkDotNet JSON reports or a directory containing them, and writes a Markdown table. A base path that does not exist reports the second run without a baseline. It exits with code 1 when allocated bytes increase by more than the threshold percentage, a benchmark in the first run is missing from the second, or a benchmark in the second run failed or has no allocation data. A time change above the threshold is reported only when the 99.9% confidence intervals do not overlap. Also review the errors and standard deviations in the CSV reports rather than only the means.

Options after the threshold:

- `--allocation-threshold <class>=<percent>` uses a different allocation threshold for the benchmarks of one class, for example `--allocation-threshold AnalyzerBenchmarks=10`. It can be repeated. Time changes still use the main threshold.
- `--accept-regressions` reports allocation regressions and removed benchmarks as accepted instead of failing. Failed benchmarks and missing allocation data in the second run still fail.

## Verify output equivalence

The verification mode compiles all scenarios at both sizes, checks for errors, and records each generated file's SHA-256 hash, keyed by scenario, size, edit state and hint name. It also records a hash of each analyzer's sorted diagnostics on the initial source, keyed by scenario, size and analyzer.

```powershell
dotnet run --project src\Ioc\test\SourceGen.Ioc.GeneratorBenchmark\SourceGen.Ioc.GeneratorBenchmark.csproj -c Release -- --verify "$env:TEMP\generator-before.json"
# Repeat on the modified version with generator-after.json.
Compare-Object (Get-Content "$env:TEMP\generator-before.json") (Get-Content "$env:TEMP\generator-after.json")
```

No differences are expected for behavior-preserving optimizations. GeneratorPerformanceTests also uses the same input builders to verify incremental cache behavior and that the analyzer scenarios are valid, without timing-sensitive assertions.

## Continuous integration

[ioc.benchmark.generator.yml](../../../../.github/workflows/ioc.benchmark.generator.yml) runs the `Count = 500` generator and analyzer workloads with the arguments above for pull requests to `main` and pushes to `main` that change the generator, its runtime library, this harness, shared build files or benchmark workflow/comment code. Changes to Markdown files (`*.md`), such as specs and this README, do not trigger it. It can also be started manually.

Each run measures the base and the head in the same job, on the same runner, and compares them. Nothing is committed to the repository.

| Event | Base | Head |
| --- | --- | --- |
| Pull request | The `main` commit that the pull request merge commit is based on | The merge commit |
| Push to `main`, manual run | The first parent of the measured commit | The measured commit |

- The base is checked out into a separate worktree and built and run with its own harness and SDK, so benchmarks that the base does not have yet, such as newly added ones, are reported as "No baseline" and do not fail the job. The same applies when the base has no harness, or its run fails.
- The base runs before the head. Both share the runner, but neighbouring load can still change during the run, so time changes only produce warnings.
- The job fails when allocated bytes increase by more than 5% (10% for `AnalyzerBenchmarks`), a base benchmark is missing from the head, or a head result failed or has no allocation data. Analyzer allocations vary by up to about 4% between identical runs because of Roslyn's analysis driver, even for `AnalyzerOverhead`, while generator allocations vary by less than 0.1%.
- The comparison is written to the job summary, followed by the BenchmarkDotNet reports of both runs. All BenchmarkDotNet output is uploaded as the `ioc-generator-benchmark-<run id>` artifact for 7 days, with the runs in `base/` and `head/`.
- For pull requests, [ioc.benchmark.generator.comment.yml](../../../../.github/workflows/ioc.benchmark.generator.comment.yml) creates or updates a single comment with the comparison. It runs from the default branch through `workflow_run`, so pull request code never receives a write token.

### Accept a regression

To accept an intentional allocation increase or a removed benchmark, add the `benchmark-accept-allocation` label to the pull request. Adding or removing that label re-runs the benchmark; other labels do not. With the label, the comparison passes `--accept-regressions` and reports these results as accepted, and the summary states that the label is applied. Failed benchmarks and missing allocation data still fail the job.

The label only affects that pull request. After the merge, pushes to `main` compare with the merged commit's first parent, so the accepted change is measured once more as an ordinary difference.
