using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.GeneratorBenchmark;

[MemoryDiagnoser]
public class GeneratorBenchmarks
{
    [Params(100, 500)]
    public int Count { get; set; }

    [ParamsAllValues]
    public GeneratorScenario Scenario { get; set; }

    private GeneratorBenchmarkInput input = null!;
    private GeneratorDriver warmDriver = null!;

    // Validating a large output compilation can take minutes, so each setup validates only the output its benchmark measures.
    [GlobalSetup(Target = nameof(Cold))]
    public void SetupCold()
    {
        input = new GeneratorBenchmarkInput(Scenario, Count);
        var driver = input.CreateDriver().RunGeneratorsAndUpdateCompilation(input.Initial, out var output, out _);
        GeneratorBenchmarkInput.Validate(driver, output);
    }

    [GlobalSetup(Target = nameof(Edit))]
    public void SetupEdit()
    {
        input = new GeneratorBenchmarkInput(Scenario, Count);
        warmDriver = input.CreateDriver().RunGenerators(input.Initial);
        var editedDriver = warmDriver.RunGeneratorsAndUpdateCompilation(input.Edited, out var editedOutput, out _);
        GeneratorBenchmarkInput.Validate(editedDriver, editedOutput);
    }

    [Benchmark]
    public GeneratorDriver Cold() => input.CreateDriver().RunGenerators(input.CreateCompilation());

    // Always start from the initial driver, not the result of the preceding benchmark invocation.
    [Benchmark]
    public GeneratorDriver Edit() => warmDriver.RunGenerators(input.Edited);
}
