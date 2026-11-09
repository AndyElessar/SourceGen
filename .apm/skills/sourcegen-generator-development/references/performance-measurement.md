# Measuring Generator Performance

Benchmarks are run only when the user asks (see the root `AGENTS.md`). These notes are for local investigations and for reading CI benchmark results.

## Avoid measuring a stale build

Each of these silently measures old code and makes a change look neutral:

- `dotnet run file.cs` (file-based apps referencing the generator with `#:project`) uses a fast up-to-date check that ignores the referenced projects' sources. Build with `dotnet build file.cs -c Release` and run the output.
- `Copy-Item` keeps the source file's `LastWriteTime`, so MSBuild skips the rebuild after swapping file versions. Touch the file (`(Get-Item f).LastWriteTime = Get-Date`) and check the timestamp of the built generator DLL.
- BenchmarkDotNet finds the benchmark project from the **current working directory**, not from `--project`. When comparing a git worktree, `cd` into the worktree first; otherwise base and head both measure the current checkout.
- A failed `dotnet publish` leaves the previous native executable in place. Check the exit code and the executable's timestamp before running it.

Sanity-check every A/B run with one benchmark whose difference is known.

## Attributing cost

- **Stage time**: enable `trackIncrementalGeneratorSteps` and read `IncrementalGeneratorRunStep.ElapsedTime`, Roslyn's own timing per pipeline step.
- **Roslyn vs generator cost**: compare the real generator with `OverheadGenerator` (the same pipeline shape without our code).
- **Superlinear stages**: scale the registration count (250 → 2000) and look for stages that more than double per doubling.
- **Local time A/B**: build base (a git worktree) and head side by side and run them interleaved. Separate BenchmarkDotNet runs hours apart drift 3–9%; `ColdOverhead` / `Rerun` benchmarks, which run no generator code, show the drift.
- **Allocations**: build a small probe executable per variant, collect with `dotnet-trace collect --profile gc-verbose`, and diff allocations per method between base and head with TraceEvent (`TraceLog.CreateFromEventPipeDataFile`, `GCAllocationTickTraceData.CallStack()`). Small uniform deltas in unchanged code are allocation-tick noise.
- `dotnet-trace`'s sampled thread time on Windows samples at GC safe points: self time lands on `PollGC` / `BulkMoveWithWriteBarrier` and allocation-heavy code looks hotter than it is. Confirm a hotspot with direct timing before optimizing it.

## Known costs

- Compiling the **generated** container can cost far more than running the generator. Nullable analysis of large generated files dominated it, which is why generated files start with `#nullable enable annotations` / `#nullable disable warnings`. Benchmark compilations use `NullableContextOptions.Disable`, so they do not show nullable-analysis effects.
- Every edit reruns all registration transforms (by `ForAttributeWithMetadataName` design).
