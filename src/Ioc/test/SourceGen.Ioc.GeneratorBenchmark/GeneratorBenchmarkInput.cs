using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc.GeneratorBenchmark;

public enum GeneratorScenario
{
    DependencyChain,
    Wrappers,
    DuplicateInvocation,
    DisabledContainer,
    RegisterFor
}

public sealed class GeneratorBenchmarkInput
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest)
        .WithPreprocessorSymbols("SOURCEGEN", "NET7_0_OR_GREATER");
    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();
    private readonly SyntaxTree declarations;
    private readonly SyntaxTree initialUsage;
    private readonly AnalyzerConfigOptionsProvider options;

    public CSharpCompilation Initial { get; }
    public CSharpCompilation Edited { get; }

    public GeneratorBenchmarkInput(GeneratorScenario scenario, int count)
    {
        var source = new StringBuilder("""
            using System;
            using System.Collections.Generic;
            using SourceGen.Ioc;
            using Microsoft.Extensions.DependencyInjection;

            """);
        for(var i = 0; i < count; i++)
        {
            if(scenario == GeneratorScenario.RegisterFor)
            {
                source.AppendLine($"[assembly: IocRegisterFor(typeof(Service{i}))]");
            }
        }
        for(var i = 0; i < count; i++)
        {
            if(scenario != GeneratorScenario.RegisterFor)
            {
                source.AppendLine("[IocRegister]");
            }
            var parameter = scenario == GeneratorScenario.DependencyChain && i + 1 < count
                ? $"Service{i + 1} dependency"
                : "";
            source.AppendLine($"public class Service{i}({parameter});");

            if(scenario is GeneratorScenario.Wrappers or GeneratorScenario.DisabledContainer)
            {
                source.AppendLine($"""
                    [IocRegister(Key = "key")]
                    public class Keyed{i};
                    [IocRegister]
                    public class Consumer{i}(Func<Service{i}> factory, Dictionary<string, Keyed{i}> keyed);
                    """);
            }
        }
        if(scenario == GeneratorScenario.DuplicateInvocation)
        {
            source.AppendLine("[IocRegister] public class Generic<T>;");
        }
        if(scenario is GeneratorScenario.Wrappers or GeneratorScenario.DisabledContainer)
        {
            source.AppendLine("[IocContainer] public partial class Container;");
        }
        declarations = CSharpSyntaxTree.ParseText(source.ToString(), ParseOptions, "Declarations.cs");
        var call = scenario == GeneratorScenario.DuplicateInvocation ? "sp.GetRequiredService<Generic<int>>();" : "";
        initialUsage = CSharpSyntaxTree.ParseText(Usage(call, 0), ParseOptions, "Usage.cs");
        var editedUsage = CSharpSyntaxTree.ParseText(Usage(call + call, 1), ParseOptions, "Usage.cs");
        options = new OptionsProvider(scenario == GeneratorScenario.DisabledContainer ? "Register" : null);
        Initial = CreateCompilation();
        Edited = Initial.ReplaceSyntaxTree(initialUsage, editedUsage);
    }

    public CSharpCompilation CreateCompilation() => CSharpCompilation.Create(
        "GeneratorPerformance", [declarations, initialUsage], References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    public GeneratorDriver CreateDriver(bool trackSteps = false) => CSharpGeneratorDriver.Create(
        [new IocSourceGenerator().AsSourceGenerator()],
        parseOptions: ParseOptions,
        optionsProvider: options,
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackSteps));

    public static void Validate(GeneratorDriver driver, Compilation output)
    {
        var result = driver.GetRunResult();
        var errors = result.Diagnostics.Concat(output.GetDiagnostics())
            .Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if(result.Results.Any(static r => r.Exception is not null) || errors.Length > 0 || result.GeneratedTrees.IsEmpty)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(static d => d.ToString())));
        }
    }

    private static string Usage(string call, int value) => $$"""
        using System;
        using Microsoft.Extensions.DependencyInjection;
        public static class Usage
        {
            public static int Run(IServiceProvider sp) { {{call}} return {{value}}; }
        }
        """;

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Runtime reference assemblies are unavailable.")).Split(Path.PathSeparator);
        return paths.Concat([
                typeof(IocRegisterAttribute).Assembly.Location,
                typeof(ServiceLifetime).Assembly.Location,
                typeof(ServiceCollection).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();
    }

    private sealed class OptionsProvider(string? features) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(features);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    private sealed class Options(string? features) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = features ?? "";
            return features is not null && key == "build_property.SourceGenIocFeatures";
        }
    }
}
