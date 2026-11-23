using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc.Test.Helpers;

/// <summary>
/// Settings of one generator run. The defaults give a library compilation named <c>TestAssembly</c>.
/// </summary>
public sealed record GeneratorRunOptions
{
    public string AssemblyName { get; init; } = "TestAssembly";

    /// <summary>References in addition to <see cref="SourceGeneratorTestHelper.BaseReferences"/>.</summary>
    public IEnumerable<MetadataReference> References { get; init; } = [];

    /// <summary>Replaces <see cref="SourceGeneratorTestHelper.BaseReferences"/>, for example to leave out MS.DI.</summary>
    public ImmutableArray<MetadataReference>? BaseReferences { get; init; }

    /// <summary>Global analyzer config options, such as <c>build_property.RootNamespace</c>.</summary>
    public IReadOnlyDictionary<string, string> GlobalOptions { get; init; } = new Dictionary<string, string>();

    /// <summary>Severity options for the whole compilation, like <c>NoWarn</c> or a global config.</summary>
    public IReadOnlyDictionary<string, ReportDiagnostic> DiagnosticOptions { get; init; } = new Dictionary<string, ReportDiagnostic>();

    /// <summary>
    /// Severity options for one file, like a file section of <c>.editorconfig</c>. The key is the file path
    /// (<see cref="SourceGeneratorTestHelper.FilePath"/>), the value maps a diagnostic ID to its severity.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ReportDiagnostic>> FileDiagnosticOptions { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, ReportDiagnostic>>();

    /// <summary>Compiler error IDs that the input source may have, such as a partial member that only the generator implements.</summary>
    public IReadOnlySet<string> AllowedInputErrors { get; init; } = new HashSet<string>();

    /// <summary>Records the steps of the incremental pipeline, for <see cref="IncrementalTestHelper"/>.</summary>
    public bool TrackSteps { get; init; }
}

/// <summary>
/// Creates compilations and runs a generator on them.
/// </summary>
public static class SourceGeneratorTestHelper
{
    /// <summary>The latest C# version, with the <c>SOURCEGEN</c> symbol that the package props define.</summary>
    public static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Preview)
        .WithPreprocessorSymbols("SOURCEGEN");

    /// <summary>The assemblies of the runtime, <c>SourceGen.Ioc</c>, and MS.DI with its abstractions.</summary>
    public static readonly ImmutableArray<MetadataReference> BaseReferences = CreateBaseReferences(includeDependencyInjection: true);

    /// <summary>The same as <see cref="BaseReferences"/>, without the <c>Microsoft.Extensions.DependencyInjection</c> package.</summary>
    public static readonly ImmutableArray<MetadataReference> BaseReferencesWithoutDependencyInjection = CreateBaseReferences(includeDependencyInjection: false);

    private static ImmutableArray<MetadataReference> CreateBaseReferences(bool includeDependencyInjection)
    {
        // Only the assemblies of the runtime directory, so that the result does not depend on what the test process loaded
        string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        IEnumerable<string> runtime = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => string.Equals(Path.GetDirectoryName(path) + Path.DirectorySeparatorChar, runtimeDirectory, StringComparison.OrdinalIgnoreCase));

        List<string> paths = [.. runtime,
            System.Reflection.Assembly.Load(new AssemblyName("SourceGen.Ioc")).Location,
            typeof(Microsoft.Extensions.DependencyInjection.ServiceLifetime).Assembly.Location];
        if(includeDependencyInjection)
        {
            paths.Add(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions).Assembly.Location);
        }

        return [.. paths.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];
    }

    /// <summary>Runs the generator on one source file.</summary>
    public static GeneratorTestResult RunGenerator<TGenerator>(string source, GeneratorRunOptions? options = null)
        where TGenerator : IIncrementalGenerator, new()
        => RunGenerator<TGenerator>([source], options);

    /// <summary>Runs the generator on several source files, named <c>File0.cs</c>, <c>File1.cs</c>, and so on.</summary>
    public static GeneratorTestResult RunGenerator<TGenerator>(IReadOnlyList<string> sources, GeneratorRunOptions? options = null)
        where TGenerator : IIncrementalGenerator, new()
    {
        options ??= new GeneratorRunOptions();
        CSharpCompilation compilation = CreateCompilation(sources, options);
        ThrowIfInputHasErrors(compilation, options.AllowedInputErrors);

        GeneratorDriver driver = CreateDriver(new TGenerator(), options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        ThrowIfGeneratorFailed(driver);
        return new GeneratorTestResult(driver, compilation, output, diagnostics);
    }

    /// <summary>
    /// Throws when a generator threw an exception. The driver reports such an exception only as the warning CS8785, so
    /// without this check a test of "no output" or "no diagnostic" would pass for a generator that crashed.
    /// </summary>
    public static void ThrowIfGeneratorFailed(GeneratorDriver driver)
    {
        if(driver.GetRunResult().Results.FirstOrDefault(static r => r.Exception is not null) is { Exception: { } exception } failed)
        {
            throw new InvalidOperationException($"Generator '{failed.Generator.GetGeneratorType().Name}' threw an exception.", exception);
        }
    }

    /// <summary>
    /// Verifies that the generator gives the same sources when it runs two times, and when the source files come in the
    /// reverse order.
    /// </summary>
    /// <param name="checkReversedOrder">
    /// Set to <see langword="false"/> when the specification lets the output depend on the order of the files, for
    /// example the order of injected members of a partial type declared in several files (Annotations.spec, "Order").
    /// </param>
    public static async Task VerifyDeterministicAsync<TGenerator>(IReadOnlyList<string> sources, GeneratorRunOptions? options = null, bool checkReversedOrder = true)
        where TGenerator : IIncrementalGenerator, new()
    {
        string first = Describe(RunGenerator<TGenerator>(sources, options));
        string second = Describe(RunGenerator<TGenerator>(sources, options));
        await Assert.That(second).IsEqualTo(first).Because("A second run gave different sources.");

        if(checkReversedOrder)
        {
            string reversed = Describe(RunGenerator<TGenerator>([.. sources.Reverse()], options));
            await Assert.That(reversed).IsEqualTo(first).Because("The reverse order of the source files gave different sources.");
        }

        static string Describe(GeneratorTestResult result) => string.Join(
            Environment.NewLine,
            result.GeneratedSources.Select(static s => $"// {s.HintName}{Environment.NewLine}{s.SourceText}"));
    }

    /// <summary>Creates the compilation that <see cref="RunGenerator{TGenerator}(IReadOnlyList{string}, GeneratorRunOptions?)"/> uses.</summary>
    public static CSharpCompilation CreateCompilation(IReadOnlyList<string> sources, GeneratorRunOptions? options = null)
    {
        options ??= new GeneratorRunOptions();
        CSharpCompilationOptions compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
            .WithSpecificDiagnosticOptions(options.DiagnosticOptions)
            .WithSyntaxTreeOptionsProvider(new FileDiagnosticOptionsProvider(options.FileDiagnosticOptions));

        return CSharpCompilation.Create(
            options.AssemblyName,
            sources.Select(static (source, i) => CSharpSyntaxTree.ParseText(source, ParseOptions, FilePath(i))),
            (options.BaseReferences ?? BaseReferences).AddRange(options.References),
            compilationOptions);
    }

    /// <summary>The path of the source file at <paramref name="index"/>.</summary>
    public static string FilePath(int index) => $"File{index}.cs";

    /// <summary>Creates a driver for <paramref name="generator"/> with the options of the run.</summary>
    public static GeneratorDriver CreateDriver(IIncrementalGenerator generator, GeneratorRunOptions options)
        => CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            parseOptions: ParseOptions,
            optionsProvider: new GlobalOptionsProvider(options.GlobalOptions),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: options.TrackSteps));

    /// <summary>
    /// Compiles an assembly that another test compilation references, such as a module. Runs the generator on it first.
    /// </summary>
    /// <returns>
    /// The generator result, and two references to the output: an emitted image, as a command-line build sees a DLL, and a
    /// compilation reference, as an IDE sees a project reference.
    /// </returns>
    public static ReferencedAssembly CompileReference<TGenerator>(string source, GeneratorRunOptions options)
        where TGenerator : IIncrementalGenerator, new()
    {
        GeneratorTestResult result = RunGenerator<TGenerator>(source, options);
        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emit = result.OutputCompilation.Emit(stream);
        if(!emit.Success)
        {
            throw new InvalidOperationException(
                $"Assembly '{options.AssemblyName}' does not compile:{Environment.NewLine}{string.Join(Environment.NewLine, emit.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error))}");
        }

        return new ReferencedAssembly(
            result,
            MetadataReference.CreateFromImage(stream.ToArray()),
            result.OutputCompilation.ToMetadataReference());
    }

    private static void ThrowIfInputHasErrors(Compilation compilation, IReadOnlySet<string> allowed)
    {
        Diagnostic[] errors = [.. compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && !allowed.Contains(d.Id))];
        if(errors.Length > 0)
        {
            throw new InvalidOperationException($"The input source has errors:{Environment.NewLine}{string.Join(Environment.NewLine, errors.AsEnumerable())}");
        }
    }
}

/// <summary>An assembly that a test compilation references.</summary>
/// <param name="Result">The generator result of the assembly.</param>
/// <param name="Image">A reference to the emitted assembly (command-line build).</param>
/// <param name="CompilationReference">A reference to the compilation (project reference in an IDE).</param>
public sealed record ReferencedAssembly(GeneratorTestResult Result, MetadataReference Image, MetadataReference CompilationReference);

file sealed class GlobalOptionsProvider(IReadOnlyDictionary<string, string> globalOptions) : AnalyzerConfigOptionsProvider
{
    private readonly DictionaryOptions global = new(globalOptions);

    public override AnalyzerConfigOptions GlobalOptions => global;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => DictionaryOptions.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => DictionaryOptions.Empty;
}

file sealed class DictionaryOptions(IReadOnlyDictionary<string, string> options) : AnalyzerConfigOptions
{
    public static readonly DictionaryOptions Empty = new(new Dictionary<string, string>());

    public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
        => options.TryGetValue(key, out value);
}

file sealed class FileDiagnosticOptionsProvider(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ReportDiagnostic>> options) : SyntaxTreeOptionsProvider
{
    public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) => GeneratedKind.Unknown;

    public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
    {
        if(options.TryGetValue(tree.FilePath, out IReadOnlyDictionary<string, ReportDiagnostic>? file) && file.TryGetValue(diagnosticId, out severity))
        {
            return true;
        }

        severity = default;
        return false;
    }

    public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
    {
        severity = default;
        return false;
    }
}
