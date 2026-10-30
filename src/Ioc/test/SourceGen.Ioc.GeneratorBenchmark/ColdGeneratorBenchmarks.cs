using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.GeneratorBenchmark;

// A cold operation allocates up to tens of MB, so a pilot-chosen operation count would change how many GCs land inside an iteration,
// which shifts time and allocations between runs. A fixed count keeps runs comparable and still averages several operations.
// The incremental benchmarks stay in GeneratorBenchmarks, where the pilot picks counts from about 8 to 1800 operations.
[MemoryDiagnoser]
[InvocationCount(4)]
public class ColdGeneratorBenchmarks
{
    [Params(100, 500)]
    public int Count { get; set; }

    // StrictContainers is analyzer-only: its generator path is covered by the other container scenarios.
    [Params(GeneratorScenario.DependencyChain, GeneratorScenario.Wrappers, GeneratorScenario.DuplicateInvocation,
        GeneratorScenario.DisabledContainer, GeneratorScenario.RegisterFor, GeneratorScenario.Realistic)]
    public GeneratorScenario Scenario { get; set; }

    private GeneratorBenchmarkInput input = null!;

    [GlobalSetup]
    public void Setup()
    {
        input = new GeneratorBenchmarkInput(Scenario, Count);
        var driver = input.CreateDriver().RunGeneratorsAndUpdateCompilation(input.Initial, out var output, out _);
        GeneratorBenchmarkInput.Validate(driver, output);
    }

    [Benchmark]
    public GeneratorDriver Cold() => input.CreateDriver().RunGenerators(input.CreateCompilation());

    // Subtract from Cold to estimate the generator's own cost.
    [Benchmark]
    public GeneratorDriver ColdOverhead() => input.CreateOverheadDriver().RunGenerators(input.CreateCompilation());
}
