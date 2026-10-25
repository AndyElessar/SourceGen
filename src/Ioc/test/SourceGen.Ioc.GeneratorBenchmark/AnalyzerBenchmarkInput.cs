using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc.GeneratorBenchmark;

public sealed class AnalyzerBenchmarkInput
{
    public static readonly ImmutableArray<DiagnosticAnalyzer> NoOp = [new NoOpAnalyzer()];
    public static readonly ImmutableArray<DiagnosticAnalyzer> Register = [new RegisterAnalyzer()];
    public static readonly ImmutableArray<DiagnosticAnalyzer> Container = [new ContainerAnalyzer()];

    private readonly Compilation compilation;
    private readonly AnalyzerOptions options;

    /// <summary>
    /// Analyzes the scenario's source without generator output. Analyzing a compilation binds every tree,
    /// and binding large generated containers would dominate the measurement (minutes per operation).
    /// </summary>
    public AnalyzerBenchmarkInput(GeneratorScenario scenario, int count)
    {
        var generatorInput = new GeneratorBenchmarkInput(scenario, count);
        compilation = generatorInput.Initial;
        options = new AnalyzerOptions([], generatorInput.ConfigOptions);
    }

    // Sequential analysis keeps the measurement independent of the runner's core count.
    public ImmutableArray<Diagnostic> Run(ImmutableArray<DiagnosticAnalyzer> analyzers) => compilation
        .WithAnalyzers(analyzers, new CompilationWithAnalyzersOptions(options, OnAnalyzerException, concurrentAnalysis: false, logAnalyzerExecutionTime: false))
        .GetAnalyzerDiagnosticsAsync()
        .GetAwaiter()
        .GetResult();

    /// <summary>
    /// Throws when an analyzer fails or reports an error, because the measured scenario must be valid input.
    /// </summary>
    public void Validate()
    {
        foreach(var analyzers in new[] { Register, Container })
        {
            var errors = Run(analyzers).Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if(errors.Length > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(static d => d.ToString())));
            }
        }
    }

    private static void OnAnalyzerException(Exception exception, DiagnosticAnalyzer analyzer, Diagnostic diagnostic)
        => throw new InvalidOperationException($"{analyzer.GetType().Name} threw: {diagnostic}", exception);

    /// <summary>
    /// Registers the same action kinds as the IoC analyzers with empty callbacks.
    /// </summary>
    // Passed directly to WithAnalyzers and never loaded as a compiler extension, so it needs no [DiagnosticAnalyzer] registration.
#pragma warning disable RS1001
    private sealed class NoOpAnalyzer : DiagnosticAnalyzer
#pragma warning restore RS1001
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [new("NOOP001", "No-op", "No-op", "Benchmark", DiagnosticSeverity.Hidden, isEnabledByDefault: true)];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(static start =>
            {
                start.RegisterSymbolAction(static _ => { }, SymbolKind.NamedType, SymbolKind.Method, SymbolKind.Property, SymbolKind.Field);
                start.RegisterSyntaxNodeAction(static _ => { }, SyntaxKind.Attribute);
                start.RegisterSemanticModelAction(static _ => { });
                start.RegisterCompilationEndAction(static _ => { });
            });
        }
    }
}
