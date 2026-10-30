using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.GeneratorBenchmark;

// An operation allocates tens of MB, so a pilot-chosen operation count would change how many GCs land inside an iteration,
// which shifts time and allocations between runs. A fixed count keeps runs comparable and still averages several operations.
[MemoryDiagnoser]
[InvocationCount(4)]
public class AnalyzerBenchmarks
{
    [Params(100, 500)]
    public int Count { get; set; }

    [Params(GeneratorScenario.DependencyChain, GeneratorScenario.Wrappers, GeneratorScenario.RegisterFor, GeneratorScenario.Realistic)]
    public GeneratorScenario Scenario { get; set; }

    private AnalyzerBenchmarkInput input = null!;

    [GlobalSetup]
    public void Setup()
    {
        input = new AnalyzerBenchmarkInput(Scenario, Count);
        input.Validate();
    }

    // Every operation analyzes a fresh analysis compilation, so each result includes the binding the analyzer actions trigger.
    // AnalyzerOverhead runs the same kinds of actions with empty callbacks; subtract it to estimate the analyzer's own cost.
    [Benchmark]
    public ImmutableArray<Diagnostic> AnalyzerOverhead() => input.Run(AnalyzerBenchmarkInput.NoOp);

    [Benchmark]
    public ImmutableArray<Diagnostic> RegisterAnalyzer() => input.Run(AnalyzerBenchmarkInput.Register);

    [Benchmark]
    public ImmutableArray<Diagnostic> ContainerAnalyzer() => input.Run(AnalyzerBenchmarkInput.Container);
}
