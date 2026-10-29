using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SourceGen.Ioc.Test.Helpers;

/// <summary>
/// Result of running a source generator, including the generated sources and output compilation.
/// </summary>
/// <param name="Result">The generator run result containing generated sources.</param>
/// <param name="OutputCompilation">The compilation after the generator has run, including generated sources.</param>
public readonly partial record struct GeneratorTestResult(GeneratorRunResult Result, Compilation OutputCompilation)
{
    /// <summary>
    /// Gets all generated source texts from the generator run result.
    /// </summary>
    public IEnumerable<(string HintName, string SourceText)> GeneratedSources =>
        Result.GeneratedSources.Select(s => (s.HintName, s.SourceText.ToString()));

    /// <summary>
    /// Gets a single generated source by hint name.
    /// </summary>
    public string? GetGeneratedSource(string hintNameContains) =>
        Result.GeneratedSources
            .FirstOrDefault(s => s.HintName.Contains(hintNameContains))
            .SourceText?.ToString();

    /// <summary>
    /// Verifies that the output compilation has no errors.
    /// Uses TUnit assertions to report compilation errors with generated source code for debugging.
    /// </summary>
    public async Task VerifyCompilableAsync()
    {
        var errors = OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if(errors.Length > 0)
        {
            var errorMessages = string.Join(Environment.NewLine, errors.Select(e => e.ToString()));

            // Include generated source code in the error message for debugging
            var generatedSourcesText = string.Join(
                Environment.NewLine + new string('=', 80) + Environment.NewLine,
                Result.GeneratedSources.Select(s =>
                    $"// File: {s.HintName}{Environment.NewLine}{AddLineNumbers(s.SourceText.ToString())}"));

            var message = $"Generated code has compilation errors:{Environment.NewLine}" +
                $"{errorMessages}{Environment.NewLine}{Environment.NewLine}" +
                $"Generated source code:{Environment.NewLine}" +
                $"{new string('=', 80)}{Environment.NewLine}" +
                $"{generatedSourcesText}";

            await Assert.That(errors).IsEmpty().Because(message);
        }

        await VerifyNoWarningsWithNullableEnabledAsync();
        await VerifyGeneratedCodeAttributesAsync();
    }

    /// <summary>
    /// Verifies that every top-level generated type has <c>[GeneratedCode]</c>, with a <c>major.minor.patch</c> version
    /// that does not include the git height.
    /// </summary>
    private async Task VerifyGeneratedCodeAttributesAsync()
    {
        var topLevelTypes = Result.GeneratedSources
            .SelectMany(source => source.SyntaxTree.GetRoot().DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>()
                .Where(type => !type.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any())
                .Select(type => (source.HintName, Type: type, Attribute: type.AttributeLists
                    .SelectMany(static l => l.Attributes)
                    .FirstOrDefault(static a => a.Name.ToString().EndsWith("GeneratedCode", StringComparison.Ordinal)))))
            .ToArray();

        var missing = topLevelTypes
            .Where(static t => t.Attribute is null)
            .Select(static t => $"{t.HintName}: {t.Type.Identifier}")
            .ToArray();

        await Assert.That(missing).IsEmpty()
            .Because($"Generated types without [GeneratedCode]:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");

        var invalidVersions = topLevelTypes
            .Where(static t => t.Attribute?.ArgumentList is { Arguments: [_, var version] }
                && !VersionRegex.IsMatch(version.ToString()))
            .Select(static t => $"{t.HintName}: {t.Type.Identifier} {t.Attribute}")
            .ToArray();

        await Assert.That(invalidVersions).IsEmpty()
            .Because($"[GeneratedCode] versions that are not major.minor.patch:{Environment.NewLine}{string.Join(Environment.NewLine, invalidVersions)}");
    }
    [GeneratedRegex("""^"\d+\.\d+\.\d+"$""")]
    private static partial Regex VersionRegex { get; }

    /// <summary>
    /// Verifies that the generated code has no warnings when compiled with nullable warnings enabled.
    /// </summary>
    /// <remarks>
    /// Generated files disable nullable warnings, because nullable flow analysis dominates the compiler's binding time of
    /// large generated files. Compiling them with <c>#nullable enable</c> keeps checking that the generated code is
    /// nullable-correct.
    /// </remarks>
    private async Task VerifyNoWarningsWithNullableEnabledAsync()
    {
        var compilation = OutputCompilation;
        var checkedTrees = new HashSet<SyntaxTree>();
        foreach(var source in Result.GeneratedSources)
        {
            var text = source.SourceText.ToString().Replace("#nullable disable warnings", "#nullable enable warnings");
            var tree = source.SyntaxTree.WithChangedText(SourceText.From(text));
            compilation = compilation.ReplaceSyntaxTree(source.SyntaxTree, tree);
            checkedTrees.Add(tree);
        }

        var warnings = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Warning && checkedTrees.Contains(d.Location.SourceTree!))
            .ToArray();

        await Assert.That(warnings).IsEmpty()
            .Because($"Generated code has warnings with nullable warnings enabled:{Environment.NewLine}{string.Join(Environment.NewLine, warnings.Select(w => w.ToString()))}");
    }

    /// <summary>
    /// Verifies that the output compilation has at least one error.
    /// Uses TUnit assertions to verify compilation failure.
    /// </summary>
    public async Task VerifyHasCompilationErrorsAsync()
    {
        var errors = OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error);

        await Assert.That(errors).IsNotEmpty().Because("Expected compilation errors but the generated code compiled successfully.");
    }

    /// <summary>
    /// Gets compilation errors from the output compilation.
    /// </summary>
    public IReadOnlyList<Diagnostic> GetCompilationErrors() =>
        [.. OutputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)];

    private static string AddLineNumbers(string source)
    {
        var lines = source.Split('\n');
        var maxLineNumberWidth = lines.Length.ToString().Length;
        return string.Join(
            Environment.NewLine,
            lines.Select((line, index) =>
                $"{(index + 1).ToString().PadLeft(maxLineNumberWidth)} | {line.TrimEnd('\r')}"));
    }
}

/// <summary>
/// Helper class for testing source generators.
/// </summary>
public static class SourceGeneratorTestHelper
{
    /// <summary>
    /// Gets the parse options for the latest C# language version with SOURCEGEN and NET7_0_OR_GREATER preprocessor definitions.
    /// </summary>
    public static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest)
        .WithPreprocessorSymbols("SOURCEGEN", "NET7_0_OR_GREATER");

    /// <summary>
    /// Gets the metadata reference for the SourceGen.Ioc assembly containing attribute definitions.
    /// </summary>
    public static readonly MetadataReference IocAttributeReference =
        MetadataReference.CreateFromFile(typeof(IocRegisterAttribute).Assembly.Location);

    /// <summary>
    /// Gets the metadata reference for the Microsoft.Extensions.DependencyInjection.Abstractions assembly.
    /// </summary>
    public static readonly MetadataReference DiAbstractionsReference =
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceLifetime).Assembly.Location);

    /// <summary>
    /// Gets the metadata reference for the Microsoft.Extensions.DependencyInjection assembly.
    /// This is needed to detect IServiceProviderFactory support.
    /// </summary>
    public static readonly MetadataReference DiReference =
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions).Assembly.Location);

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
            .Cast<MetadataReference>()
            .ToList();

        // Ensure ValueTask is available (from System.Threading.Tasks.Extensions or System.Runtime)
        var valueTaskAssembly = typeof(ValueTask).Assembly;
        if(!string.IsNullOrEmpty(valueTaskAssembly.Location))
        {
            references.Add(MetadataReference.CreateFromFile(valueTaskAssembly.Location));
        }

        return [.. references, IocAttributeReference, DiAbstractionsReference, DiReference];
    }

    /// <summary>
    /// Gets the base metadata references WITHOUT the full Microsoft.Extensions.DependencyInjection package.
    /// Use this to test scenarios where IServiceProviderFactory should not be generated.
    /// </summary>
    public static readonly ImmutableArray<MetadataReference> BaseReferencesWithoutDI = CreateBaseReferencesWithoutDI();
    private static ImmutableArray<MetadataReference> CreateBaseReferencesWithoutDI()
    {
        // Filter out the full DI package (but keep Abstractions)
        var diAssemblyName = typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions).Assembly.GetName().Name;

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Where(a => a.GetName().Name != diAssemblyName)
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>();

        // Only include Abstractions, not the full DI package
        return [.. references, IocAttributeReference, DiAbstractionsReference];
    }

    /// <summary>
    /// Runs the source generator and returns the generated source texts.
    /// </summary>
    /// <param name="source">The source code to compile.</param>
    /// <param name="assemblyName">The assembly name for the compilation.</param>
    /// <param name="additionalReferences">Optional additional metadata references.</param>
    /// <param name="analyzerConfigOptions">Optional analyzer config options (e.g., MSBuild properties).</param>
    public static GeneratorTestResult RunGenerator<TGenerator>(
        string source,
        string assemblyName = "TestAssembly",
        IEnumerable<MetadataReference>? additionalReferences = null,
        IReadOnlyDictionary<string, string>? analyzerConfigOptions = null,
        IReadOnlySet<string>? suppressedInitialDiagnosticIds = null)
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

        ThrowIfCompilationHasErrors(compilation, suppressedInitialDiagnosticIds);

        var generator = new TGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator).WithUpdatedParseOptions(ParseOptions);

        // Apply analyzer config options if provided
        if(analyzerConfigOptions is not null)
        {
            var optionsProvider = new TestAnalyzerConfigOptionsProvider(analyzerConfigOptions);
            driver = driver.WithUpdatedAnalyzerConfigOptions(optionsProvider);
        }

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        return new GeneratorTestResult(driver.GetRunResult().Results.Single(), outputCompilation);
    }

    /// <summary>
    /// Runs the source generator on several source files, one syntax tree per file.
    /// Use this to test declarations split across files, such as partial types.
    /// </summary>
    /// <param name="sources">The source code of each file.</param>
    public static GeneratorTestResult RunGeneratorOnFiles<TGenerator>(params string[] sources)
        where TGenerator : IIncrementalGenerator, new()
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            sources.Select(static (source, i) => CSharpSyntaxTree.ParseText(source, ParseOptions, $"File{i}.cs")),
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ThrowIfCompilationHasErrors(compilation);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new TGenerator()).WithUpdatedParseOptions(ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        return new GeneratorTestResult(driver.GetRunResult().Results.Single(), outputCompilation);
    }

    /// <summary>
    /// Runs the source generator with custom base references.
    /// Use this to test scenarios with different reference configurations.
    /// </summary>
    /// <param name="source">The source code to compile.</param>
    /// <param name="references">The metadata references to use for compilation.</param>
    /// <param name="assemblyName">The assembly name for the compilation.</param>
    public static GeneratorTestResult RunGeneratorWithReferences<TGenerator>(
        string source,
        ImmutableArray<MetadataReference> references,
        string assemblyName = "TestAssembly")
        where TGenerator : IIncrementalGenerator, new()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ThrowIfCompilationHasErrors(compilation);

        var generator = new TGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator).WithUpdatedParseOptions(ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        return new GeneratorTestResult(driver.GetRunResult().Results.Single(), outputCompilation);
    }

    /// <summary>
    /// Runs the source generator on multiple assemblies where the last assembly references all previous ones.
    /// This simulates cross-assembly scenarios where one assembly imports from others.
    /// Assemblies are compiled in order, allowing later ones to reference earlier ones.
    /// The last assembly in the array is treated as the main assembly.
    /// </summary>
    /// <param name="assemblies">Assemblies to compile in order. The last one is the main assembly that references all others.</param>
    /// <returns>The generator run result for the main (last) assembly.</returns>
    public static GeneratorTestResult RunGeneratorWithDependencies<TGenerator>(
        params (string Source, string AssemblyName)[] assemblies)
        where TGenerator : IIncrementalGenerator, new()
    {
        if(assemblies.Length == 0)
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));

        var compiledReferences = new List<MetadataReference>();
        GeneratorTestResult lastResult = default;

        // Compile each assembly in order (allowing later ones to reference earlier ones)
        for(var i = 0; i < assemblies.Length; i++)
        {
            var (source, assemblyName) = assemblies[i];
            var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [syntaxTree],
                BaseReferences.AddRange(compiledReferences),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            ThrowIfCompilationHasErrors(compilation);

            var generator = new TGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator).WithUpdatedParseOptions(ParseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

            ThrowIfCompilationHasErrors((CSharpCompilation)outputCompilation);

            lastResult = new GeneratorTestResult(driver.GetRunResult().Results.Single(), outputCompilation);
            compiledReferences.Add(outputCompilation.ToMetadataReference());
        }

        return lastResult;
    }

    private static void ThrowIfCompilationHasErrors(
        CSharpCompilation compilation,
        IReadOnlySet<string>? suppressedDiagnosticIds = null)
    {
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => suppressedDiagnosticIds is null || !suppressedDiagnosticIds.Contains(d.Id))
            .ToList();

        if(errors.Count > 0)
        {
            var errorMessages = string.Join(Environment.NewLine, errors.Select(e => e.ToString()));
            throw new InvalidOperationException($"Compilation has errors:{Environment.NewLine}{errorMessages}");
        }
    }

    /// <summary>
    /// Gets all generated source texts from the generator test result.
    /// </summary>
    public static IEnumerable<(string HintName, string SourceText)> GetGeneratedSources(GeneratorTestResult result) =>
        result.GeneratedSources;

    /// <summary>
    /// Gets a single generated source by hint name.
    /// </summary>
    public static string? GetGeneratedSource(GeneratorTestResult result, string hintNameContains) =>
        result.GetGeneratedSource(hintNameContains);

    /// <summary>
    /// Runs the analyzer and returns the diagnostics.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync<TAnalyzer>(
        string source,
        string assemblyName = "TestAssembly",
        IReadOnlyDictionary<string, string>? analyzerConfigOptions = null)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new TAnalyzer();
        var analyzerOptions = analyzerConfigOptions is null
            ? null
            : new AnalyzerOptions([], new TestAnalyzerConfigOptionsProvider(analyzerConfigOptions));
        var compilationWithAnalyzers = compilation.WithAnalyzers([analyzer], options: analyzerOptions);

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
