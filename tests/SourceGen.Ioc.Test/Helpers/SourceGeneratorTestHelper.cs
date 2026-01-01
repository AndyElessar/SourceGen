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
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    /// <summary>
    /// Gets the metadata reference for the SourceGen.Ioc assembly containing attribute definitions.
    /// </summary>
    private static readonly MetadataReference IocAttributeReference =
        MetadataReference.CreateFromFile(typeof(IoCRegisterAttribute).Assembly.Location);

    /// <summary>
    /// Runs the source generator and returns the generated source texts.
    /// </summary>
    public static GeneratorRunResult RunGenerator<TGenerator>(string source, string assemblyName = "TestAssembly")
        where TGenerator : IIncrementalGenerator, new()
    {
        var userSyntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Add reference to SourceGen.Ioc assembly containing attribute definitions
        references.Add(IocAttributeReference);

        // Add reference to Microsoft.Extensions.DependencyInjection.Abstractions
        var diAbstractionsAssembly = typeof(Microsoft.Extensions.DependencyInjection.ServiceLifetime).Assembly;
        references.Add(MetadataReference.CreateFromFile(diAbstractionsAssembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [userSyntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Check for compilation errors before running generator
        var compilationDiagnostics = compilation.GetDiagnostics();
        var errors = compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            var errorMessages = string.Join(Environment.NewLine, errors.Select(e => e.ToString()));
            throw new InvalidOperationException($"Compilation has errors:{Environment.NewLine}{errorMessages}");
        }

        var generator = new TGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator).WithUpdatedParseOptions(ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        return runResult.Results.Single();
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
    public static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync<TAnalyzer>(string source, string assemblyName = "TestAssembly")
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var userSyntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Add reference to SourceGen.Ioc assembly containing attribute definitions
        references.Add(IocAttributeReference);

        // Add reference to Microsoft.Extensions.DependencyInjection.Abstractions
        var diAbstractionsAssembly = typeof(Microsoft.Extensions.DependencyInjection.ServiceLifetime).Assembly;
        references.Add(MetadataReference.CreateFromFile(diAbstractionsAssembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [userSyntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new TAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics;
    }

    /// <summary>
    /// Gets diagnostics with a specific ID.
    /// </summary>
    public static IEnumerable<Diagnostic> GetDiagnosticsById(ImmutableArray<Diagnostic> diagnostics, string diagnosticId)
    {
        return diagnostics.Where(d => d.Id == diagnosticId);
    }
}
