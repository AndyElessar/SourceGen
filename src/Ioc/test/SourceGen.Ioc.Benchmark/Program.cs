
// dotnet run -c Release -f net10.0 --runtimes nativeaot10.0

using SourceGen.Ioc.Benchmark.Benchmarks;

// Choose which benchmark to run:
//BenchmarkRunner.Run<MSDI_RegistrationBenchmark>();
//BenchmarkRunner.Run<Switch_Vs_FrozenDictionaryBenchmark>();
//BenchmarkRunner.Run<ThreadSafeStrategyBenchmark>();
BenchmarkRunner.Run<RealisticAppBenchmark>();
