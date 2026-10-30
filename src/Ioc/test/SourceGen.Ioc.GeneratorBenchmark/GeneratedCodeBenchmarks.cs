using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace SourceGen.Ioc.GeneratorBenchmark;

/// <summary>
/// Measures how much compiling the generated code adds to the user's build. Subtract <see cref="Compile"/> from
/// <see cref="CompileGenerated"/> to estimate the cost of the generated code.
/// </summary>
// Compiling a large generated container takes minutes, so only the 100-unit workloads run, and CI (Count 500) skips them.
[MemoryDiagnoser]
public class GeneratedCodeBenchmarks
{
    [Params(100)]
    public int Count { get; set; }

    // StrictContainers is analyzer-only: its generator path is covered by the other container scenarios.
    [Params(GeneratorScenario.DependencyChain, GeneratorScenario.Wrappers, GeneratorScenario.DuplicateInvocation,
        GeneratorScenario.DisabledContainer, GeneratorScenario.RegisterFor, GeneratorScenario.Realistic)]
    public GeneratorScenario Scenario { get; set; }

    private GeneratorBenchmarkInput input = null!;
    private SyntaxTree[] generated = null!;

    [GlobalSetup]
    public void Setup()
    {
        input = new GeneratorBenchmarkInput(Scenario, Count);
        var driver = input.CreateDriver().RunGeneratorsAndUpdateCompilation(input.Initial, out var output, out _);
        GeneratorBenchmarkInput.Validate(driver, output);
        generated = [.. driver.GetRunResult().GeneratedTrees];
    }

    // Every operation compiles a fresh compilation, so no binding is cached between operations.
    [Benchmark]
    public EmitResult Compile() => input.CreateCompilation().Emit(Stream.Null);

    [Benchmark]
    public EmitResult CompileGenerated() => input.CreateCompilation().AddSyntaxTrees(generated).Emit(Stream.Null);
}
