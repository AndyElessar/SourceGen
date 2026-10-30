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
    private Compilation edited = null!;

    // Validating a large output compilation can take minutes, so each setup validates only the output its benchmark measures.
    [GlobalSetup(Target = nameof(Rerun))]
    public void SetupRerun()
    {
        input = new GeneratorBenchmarkInput(Scenario, Count);
        warmDriver = input.CreateDriver().RunGeneratorsAndUpdateCompilation(input.Initial, out var output, out _);
        GeneratorBenchmarkInput.Validate(warmDriver, output);
    }

    [GlobalSetup(Target = nameof(Edit))]
    public void SetupEdit() => SetupEdited(GeneratorEdit.Unrelated);

    [GlobalSetup(Target = nameof(EditConstructor))]
    public void SetupEditConstructor() => SetupEdited(GeneratorEdit.Constructor);

    [GlobalSetup(Target = nameof(EditAddRegistration))]
    public void SetupEditAddRegistration() => SetupEdited(GeneratorEdit.AddRegistration);

    [GlobalSetup(Target = nameof(EditDefaults))]
    public void SetupEditDefaults() => SetupEdited(GeneratorEdit.Defaults);

    // Runs the warm driver again on the same compilation: the lower bound of an incremental run.
    [Benchmark]
    public GeneratorDriver Rerun() => warmDriver.RunGenerators(input.Initial);

    // Edit benchmarks always start from the initial driver, not the result of the preceding benchmark invocation.
    [Benchmark]
    public GeneratorDriver Edit() => warmDriver.RunGenerators(edited);

    [Benchmark]
    public GeneratorDriver EditConstructor() => warmDriver.RunGenerators(edited);

    [Benchmark]
    public GeneratorDriver EditAddRegistration() => warmDriver.RunGenerators(edited);

    [Benchmark]
    public GeneratorDriver EditDefaults() => warmDriver.RunGenerators(edited);

    private void SetupEdited(GeneratorEdit edit)
    {
        input = new GeneratorBenchmarkInput(Scenario, Count);
        warmDriver = input.CreateDriver().RunGenerators(input.Initial);
        edited = input.CreateEdited(edit);
        var editedDriver = warmDriver.RunGeneratorsAndUpdateCompilation(edited, out var editedOutput, out _);
        GeneratorBenchmarkInput.Validate(editedDriver, editedOutput);
    }
}
