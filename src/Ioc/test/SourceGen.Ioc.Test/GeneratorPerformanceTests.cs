using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceGen.Ioc.GeneratorBenchmark;

namespace SourceGen.Ioc.Test;

public class GeneratorPerformanceTests
{
    [Test]
    [Arguments(GeneratorScenario.DependencyChain)]
    [Arguments(GeneratorScenario.Wrappers)]
    [Arguments(GeneratorScenario.DuplicateInvocation)]
    [Arguments(GeneratorScenario.DisabledContainer)]
    [Arguments(GeneratorScenario.RegisterFor)]
    public async Task Generator_UnrelatedOrDuplicateEdit_CachesRegistrations(GeneratorScenario scenario)
    {
        var input = new GeneratorBenchmarkInput(scenario, 20);
        var driver = input.CreateDriver(trackSteps: true)
            .RunGeneratorsAndUpdateCompilation(input.Initial, out var initialOutput, out _);
        GeneratorBenchmarkInput.Validate(driver, initialOutput);
        var initialSources = GetSources(driver);

        driver = driver.RunGeneratorsAndUpdateCompilation(input.Edited, out var editedOutput, out _);
        GeneratorBenchmarkInput.Validate(driver, editedOutput);

        await Assert.That(GetSources(driver)).IsEqualTo(initialSources);
        await Assert.That(driver.GetRunResult().Results.Single().TrackedSteps["ServiceRegistrations"]
            .SelectMany(static step => step.Outputs).All(static output => output.Reason == IncrementalStepRunReason.Cached)).IsTrue();
    }

    [Test]
    public async Task Generator_DisabledContainers_SkipsBothGroupingBranches()
    {
        var input = new GeneratorBenchmarkInput(GeneratorScenario.DisabledContainer, 2);
        var compilation = input.Initial.AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            using SourceGen.Ioc;
            [IocContainer(ExplicitOnly = true)]
            [IocRegisterFor<Service0>]
            public partial class ExplicitContainer;
            """, (CSharpParseOptions)input.Initial.SyntaxTrees.First().Options));
        var driver = input.CreateDriver(trackSteps: true)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        GeneratorBenchmarkInput.Validate(driver, output);

        var result = driver.GetRunResult().Results.Single();
        foreach(var name in new[] { "ContainerGroups", "ExplicitContainerGroups" })
        {
            var count = result.TrackedSteps.TryGetValue(name, out var steps)
                ? steps.Sum(static step => step.Outputs.Length)
                : 0;
            await Assert.That(count).IsEqualTo(0);
        }
        await Assert.That(result.GeneratedSources.Any(static source => source.HintName.EndsWith(".Container.g.cs"))).IsFalse();
    }

    [Test]
    public async Task Generator_DependencyConstructorChanges_DoesNotChangeConsumerModel()
    {
        const string source = """
            using SourceGen.Ioc;
            [IocRegister] public class Consumer(Dependency dependency);
            public class Dependency(int value);
            """;
        var tree = CSharpSyntaxTree.ParseText(source, SourceGeneratorTestHelper.ParseOptions);
        var compilation = CSharpCompilation.Create("ShallowParameters", [tree], SourceGeneratorTestHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new IocSourceGenerator().AsSourceGenerator()],
            parseOptions: SourceGeneratorTestHelper.ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var initialOutput, out _);
        GeneratorBenchmarkInput.Validate(driver, initialOutput);
        var sources = GetSources(driver);

        var edited = compilation.ReplaceSyntaxTree(tree,
            CSharpSyntaxTree.ParseText(source.Replace("int value", "string value"), SourceGeneratorTestHelper.ParseOptions));
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out var editedOutput, out _);
        GeneratorBenchmarkInput.Validate(driver, editedOutput);

        await Assert.That(GetSources(driver)).IsEqualTo(sources);
        await Assert.That(driver.GetRunResult().Results.Single().TrackedSteps["RegistrationData"]
            .SelectMany(static step => step.Outputs)
            .All(static output => output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsTrue();
    }

    private static string GetSources(GeneratorDriver driver) => string.Join("\n",
        driver.GetRunResult().Results.Single().GeneratedSources
            .OrderBy(static source => source.HintName, StringComparer.Ordinal)
            .Select(static source => $"{source.HintName}\n{source.SourceText}"));
}
