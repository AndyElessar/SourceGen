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
    RegisterFor,
    Realistic
}

/// <summary>
/// An edit applied to the initial compilation of a scenario.
/// </summary>
public enum GeneratorEdit
{
    /// <summary>Changes an unrelated method body; DuplicateInvocation adds a duplicate closed-generic lookup instead.</summary>
    Unrelated,
    /// <summary>Adds a constructor dependency to one registered service.</summary>
    Constructor,
    /// <summary>Adds a file with a new registration.</summary>
    AddRegistration,
    /// <summary>Changes the lifetime of the assembly-level <c>[IocRegisterDefaults]</c>.</summary>
    Defaults
}

/// <summary>
/// Builds a scenario as one syntax tree per service unit plus shared files, so per-tree caching behaves as in a real project.
/// </summary>
public sealed class GeneratorBenchmarkInput
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using SourceGen.Ioc;
        using Microsoft.Extensions.DependencyInjection;

        """;

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest)
        .WithPreprocessorSymbols("SOURCEGEN", "NET7_0_OR_GREATER");
    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();
    private readonly GeneratorScenario scenario;
    private readonly int count;
    private readonly string call;
    private readonly SyntaxTree[] units;
    private readonly SyntaxTree defaults;
    private readonly SyntaxTree usage;
    private readonly SyntaxTree[] trees;
    private readonly AnalyzerConfigOptionsProvider options;

    public CSharpCompilation Initial { get; }
    public AnalyzerConfigOptionsProvider ConfigOptions => options;

    /// <param name="count">The number of service units; at least 3, or 10 for <see cref="GeneratorScenario.Realistic"/>, so the constructor edit has a valid target.</param>
    public GeneratorBenchmarkInput(GeneratorScenario scenario, int count)
    {
        this.scenario = scenario;
        this.count = count;
        call = scenario == GeneratorScenario.DuplicateInvocation ? "sp.GetRequiredService<Generic<int>>();" : "";
        units = [.. Enumerable.Range(0, count).Select(i => Parse(Unit(i, extraDependency: false), $"Service{i}.cs"))];
        defaults = Parse(Defaults(ServiceLifetime.Singleton), "Defaults.cs");
        usage = Parse(Usage(call, 0), "Usage.cs");

        List<SyntaxTree> all = [.. units, defaults, usage];
        if(scenario == GeneratorScenario.DuplicateInvocation)
        {
            all.Add(Parse("[IocRegister] public class Generic<T>;", "Generic.cs"));
        }
        if(scenario is not (GeneratorScenario.DuplicateInvocation or GeneratorScenario.RegisterFor))
        {
            all.Add(Parse("[IocContainer] public partial class Container;", "Container.cs"));
        }
        trees = [.. all];
        options = new OptionsProvider(scenario == GeneratorScenario.DisabledContainer ? "Register" : null);
        Initial = CreateCompilation();
    }

    public CSharpCompilation CreateCompilation() => CSharpCompilation.Create(
        "GeneratorPerformance", trees, References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>
    /// Creates a new compilation that applies <paramref name="edit"/> to <see cref="Initial"/>.
    /// </summary>
    public CSharpCompilation CreateEdited(GeneratorEdit edit) => edit switch
    {
        GeneratorEdit.Unrelated => Initial.ReplaceSyntaxTree(usage, Parse(Usage(call + call, 1), "Usage.cs")),
        GeneratorEdit.Constructor => Initial.ReplaceSyntaxTree(units[EditedUnit], Parse(Unit(EditedUnit, extraDependency: true), $"Service{EditedUnit}.cs")),
        GeneratorEdit.AddRegistration => Initial.AddSyntaxTrees(Parse(scenario == GeneratorScenario.RegisterFor
            ? "[assembly: IocRegisterFor(typeof(AddedService))]\npublic class AddedService;"
            : "[IocRegister] public class AddedService;", "Added.cs")),
        GeneratorEdit.Defaults => Initial.ReplaceSyntaxTree(defaults, Parse(Defaults(ServiceLifetime.Scoped), "Defaults.cs")),
        _ => throw new ArgumentOutOfRangeException(nameof(edit), edit, null),
    };

    public GeneratorDriver CreateDriver(bool trackSteps = false) => CreateDriver(new IocSourceGenerator(), trackSteps);

    /// <summary>
    /// Creates a driver for <see cref="OverheadGenerator"/>, which subscribes to the same inputs with empty transforms.
    /// </summary>
    public GeneratorDriver CreateOverheadDriver() => CreateDriver(new OverheadGenerator(), trackSteps: false);

    private GeneratorDriver CreateDriver(IIncrementalGenerator generator, bool trackSteps) => CSharpGeneratorDriver.Create(
        [generator.AsSourceGenerator()],
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

    // Realistic units cycle through five kinds; a multiple of five is a plain service whose constructor takes extra parameters.
    private int EditedUnit => scenario == GeneratorScenario.Realistic ? count / 2 / 5 * 5 : count / 2;

    private string Unit(int i, bool extraDependency)
    {
        var source = new StringBuilder(Usings);
        if(scenario == GeneratorScenario.Realistic)
        {
            AppendRealisticUnit(source, i, extraDependency);
            return source.ToString();
        }

        source.AppendLine(scenario == GeneratorScenario.RegisterFor ? $"[assembly: IocRegisterFor(typeof(Service{i}))]" : "[IocRegister]");
        List<string> parameters = [];
        if(scenario == GeneratorScenario.DependencyChain && i + 1 < count)
        {
            parameters.Add($"Service{i + 1} dependency");
        }
        // The last service has no dependencies, so the dependency cannot form a cycle.
        if(extraDependency)
        {
            parameters.Add($"Service{count - 1} extra");
        }
        source.AppendLine($"public class Service{i}({string.Join(", ", parameters)});");

        if(scenario is GeneratorScenario.Wrappers or GeneratorScenario.DisabledContainer)
        {
            source.AppendLine($"""
                [IocRegister(Key = "key")]
                public class Keyed{i};
                [IocRegister]
                public class Consumer{i}(Func<Service{i}> factory, Dictionary<string, Keyed{i}> keyed);
                """);
        }
        return source.ToString();
    }

    /// <summary>
    /// Appends one unit of a mixed project: an interface hierarchy, a request type, every tenth unit an open generic handler,
    /// and a service whose kind cycles through plain, factory, decorated, keyed and tagged with member injection.
    /// Each service depends on earlier units only, so the graph has no cycles, and every service a unit depends on is a singleton.
    /// </summary>
    private static void AppendRealisticUnit(StringBuilder source, int i, bool extraDependency)
    {
        var decade = i / 10 * 10;
        source.AppendLine($$"""
            public interface IServiceBase{{i}};
            public interface IService{{i}} : IServiceBase{{i}};
            public sealed record Request{{i}};
            """);
        if(i == decade)
        {
            source.AppendLine($$"""
                public interface IHandler{{i}}<T>;
                [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler{{i}}<>)])]
                public sealed class Handler{{i}}<T> : IHandler{{i}}<T>;
                """);
        }

        switch(i % 5)
        {
            case 0:
                // The tagged unit before is a leaf, so depend on the keyed one.
                List<string> parameters = [$"IHandler{decade}<Request{i}> handler"];
                if(i >= 2)
                {
                    parameters.Add($"[FromKeyedServices(\"key{i - 2}\")] IService{i - 2} dependency");
                }
                if(i >= 5)
                {
                    parameters.AddRange([$"Lazy<IService{i - 5}> lazy", $"Func<IService{i - 5}> factory", $"IEnumerable<IService{i - 5}> all"]);
                }
                if(extraDependency)
                {
                    parameters.Add("IService0 extra");
                }
                source.AppendLine($$"""
                    [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IService{{i}})])]
                    public sealed class Service{{i}}({{string.Join(", ", parameters)}}) : IService{{i}};
                    """);
                break;
            case 1:
                source.AppendLine($$"""
                    [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IService{{i}})], Factory = nameof(Service{{i}}Factory.Create))]
                    public sealed class Service{{i}} : IService{{i}};
                    public static class Service{{i}}Factory
                    {
                        public static IService{{i}} Create(IService{{i - 1}} dependency, IServiceProvider provider)
                        {
                            provider.GetRequiredService<IHandler{{decade}}<Request{{i}}>>();
                            return new Service{{i}}();
                        }
                    }
                    """);
                break;
            case 2:
                source.AppendLine($$"""
                    [IocDiscover<IHandler{{decade}}<Request{{i}}>>]
                    [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IService{{i}})], Decorators = [typeof(Service{{i}}Decorator)])]
                    public sealed class Service{{i}}(IService{{i - 1}} dependency) : IService{{i}};
                    public sealed class Service{{i}}Decorator(IService{{i}} inner) : IService{{i}};
                    """);
                break;
            case 3:
                source.AppendLine($$"""
                    [IocRegister(ServiceLifetime.Singleton, RegisterAllInterfaces = true, Key = "key{{i}}")]
                    public sealed class Service{{i}}(IService{{i - 1}} dependency) : IService{{i}};
                    """);
                break;
            default:
                // Lifetime comes from [IocRegisterDefaults] for IDefaultsTarget. No service depends on a tagged one, so it may be shorter-lived.
                source.AppendLine($$"""
                    [IocRegister(ServiceTypes = [typeof(IService{{i}})], Tags = ["Tag{{i % 3}}"])]
                    public sealed class Service{{i}}([FromKeyedServices("key{{i - 1}}")] IService{{i - 1}} dependency) : IService{{i}}, IDefaultsTarget
                    {
                        [IocInject] public IService{{i - 4}} Property { get; set; } = default!;
                        [IocInject] public void Initialize(IService{{i - 3}} other) { }
                    }
                    """);
                break;
        }
    }

    private static string Defaults(ServiceLifetime lifetime) => $"""
        {Usings}
        [assembly: IocRegisterDefaults(typeof(IDefaultsTarget), ServiceLifetime.{lifetime})]
        public interface IDefaultsTarget;
        """;

    private static string Usage(string call, int value) => $$"""
        using System;
        using Microsoft.Extensions.DependencyInjection;
        public static class Usage
        {
            public static int Run(IServiceProvider sp) { {{call}} return {{value}}; }
        }
        """;

    private static SyntaxTree Parse(string source, string path) => CSharpSyntaxTree.ParseText(
        source.StartsWith("using", StringComparison.Ordinal) ? source : Usings + source, ParseOptions, path);

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
