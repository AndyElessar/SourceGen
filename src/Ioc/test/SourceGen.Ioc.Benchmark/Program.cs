using SourceGen.Ioc.Benchmark.Benchmarks;

// dotnet run --project src/Ioc/test/SourceGen.Ioc.Benchmark/SourceGen.Ioc.Benchmark.csproj -c Release -f net10.0 -- --runtimes nativeaot10.0

var benchmarkSwitcher = BenchmarkSwitcher.FromTypes(
[
	typeof(ThreadSafeStrategyBenchmark),
	typeof(RealisticAppBenchmark),
]);

benchmarkSwitcher.Run(args);
