using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc.Test.Helpers;

/// <summary>
/// Helper class for testing source generators.
/// </summary>
public static class SourceGeneratorTestHelper
{
    /// <summary>
    /// Gets the parse options for the latest C# language version.
    /// </summary>
    public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    /// <summary>
    /// Gets the metadata reference for the SourceGen.Ioc assembly containing attribute definitions.
    /// </summary>
    public static readonly MetadataReference IocAttributeReference =
        MetadataReference.CreateFromFile(typeof(IoCRegisterAttribute).Assembly.Location);

    /// <summary>
    /// Gets the metadata reference for the Microsoft.Extensions.DependencyInjection.Abstractions assembly.
    /// </summary>
    public static readonly MetadataReference DiAbstractionsReference =
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceLifetime).Assembly.Location);

    /// <summary>
    /// Gets the base metadata references from the current AppDomain assemblies,
    /// including IoC attributes and DI abstractions.
    /// </summary>
    public static readonly ImmutableArray<MetadataReference> BaseReferences = CreateBaseReferences();
    private static ImmutableArray<MetadataReference> CreateBaseReferences()
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>();

        return [.. references, IocAttributeReference, DiAbstractionsReference];
    }

    /// <summary>
    /// Runs the source generator and returns the generated source texts.
    /// </summary>
    /// <param name="source">The source code to compile.</param>
    /// <param name="assemblyName">The assembly name for the compilation.</param>
    /// <param name="additionalReferences">Optional additional metadata references.</param>
    /// <param name="analyzerConfigOptions">Optional analyzer config options (e.g., MSBuild properties).</param>
    public static GeneratorRunResult RunGenerator<TGenerator>(
        string source,
        string assemblyName = "TestAssembly",
        IEnumerable<MetadataReference>? additionalReferences = null,
        IReadOnlyDictionary<string, string>? analyzerConfigOptions = null)
        where TGenerator : IIncrementalGenerator, new()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        var references = additionalReferences is null
            ? BaseReferences
            : BaseReferences.AddRange(additionalReferences);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ThrowIfCompilationHasErrors(compilation);

        var generator = new TGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator).WithUpdatedParseOptions(ParseOptions);

        // Apply analyzer config options if provided
        if(analyzerConfigOptions is not null)
        {
            var optionsProvider = new TestAnalyzerConfigOptionsProvider(analyzerConfigOptions);
            driver = driver.WithUpdatedAnalyzerConfigOptions(optionsProvider);
        }

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        return driver.GetRunResult().Results.Single();
    }

    private static void ThrowIfCompilationHasErrors(CSharpCompilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            var errorMessages = string.Join(Environment.NewLine, errors.Select(e => e.ToString()));
            throw new InvalidOperationException($"Compilation has errors:{Environment.NewLine}{errorMessages}");
        }
    }

    /// <summary>
    /// Gets all generated source texts from the generator run result.
    /// </summary>
    public static IEnumerable<(string HintName, string SourceText)> GetGeneratedSources(GeneratorRunResult result)
    {
        return result.GeneratedSources
            .Select(s => (s.HintName, s.SourceText.ToString()));
    }

    /// <summary>
    /// Gets a single generated source by hint name.
    /// </summary>
    public static string? GetGeneratedSource(GeneratorRunResult result, string hintNameContains)
    {
        return result.GeneratedSources
            .FirstOrDefault(s => s.HintName.Contains(hintNameContains))
            .SourceText?.ToString();
    }

    /// <summary>
    /// Runs the analyzer and returns the diagnostics.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync<TAnalyzer>(
        string source,
        string assemblyName = "TestAssembly")
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new TAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    /// <summary>
    /// Gets diagnostics with a specific ID.
    /// </summary>
    public static IEnumerable<Diagnostic> GetDiagnosticsById(ImmutableArray<Diagnostic> diagnostics, string diagnosticId)
    {
        return diagnostics.Where(d => d.Id == diagnosticId);
    }

    /// <summary>
    /// Creates a CSharpCompilation from source code.
    /// </summary>
    /// <param name="assemblyName">The assembly name for the compilation.</param>
    /// <param name="source">The source code to compile.</param>
    /// <param name="additionalReferences">Optional additional metadata references.</param>
    public static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        var references = additionalReferences is null
            ? BaseReferences
            : BaseReferences.AddRange(additionalReferences);

        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

/// <summary>
/// Test implementation of AnalyzerConfigOptionsProvider for providing MSBuild properties in tests.
/// </summary>
file sealed class TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> globalOptions) : AnalyzerConfigOptionsProvider
{
    private readonly TestAnalyzerConfigOptions _globalOptions = new(globalOptions);

    public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => TestAnalyzerConfigOptions.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => TestAnalyzerConfigOptions.Empty;
}

/// <summary>
/// Test implementation of AnalyzerConfigOptions.
/// </summary>
file sealed class TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> options) : AnalyzerConfigOptions
{
    public static readonly TestAnalyzerConfigOptions Empty = new(new Dictionary<string, string>());

    public override bool TryGetValue(string key, out string value)
    {
        if(options.TryGetValue(key, out var result))
        {
            value = result;
            return true;
        }
        value = string.Empty;
        return false;
    }
}
