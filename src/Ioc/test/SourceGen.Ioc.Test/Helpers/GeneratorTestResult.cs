using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SourceGen.Ioc.Test.Helpers;

/// <summary>
/// The result of one generator run.
/// </summary>
/// <param name="Driver">The driver after the run. Run it again on an edited compilation to test incremental behavior.</param>
/// <param name="InputCompilation">The compilation before the run.</param>
/// <param name="OutputCompilation">The compilation with the generated sources.</param>
/// <param name="Diagnostics">The diagnostics of the generator, after <c>#pragma</c> and the severity options are applied.</param>
public sealed record GeneratorTestResult(
    GeneratorDriver Driver,
    Compilation InputCompilation,
    Compilation OutputCompilation,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public GeneratorRunResult RunResult => Driver.GetRunResult().Results.Single();

    /// <summary>The generated sources, ordered by hint name.</summary>
    public IReadOnlyList<GeneratedSourceResult> GeneratedSources =>
        [.. RunResult.GeneratedSources.OrderBy(static s => s.HintName, StringComparer.Ordinal)];

    /// <summary>
    /// The generated source whose hint name contains <paramref name="hintNameContains"/>, or <see langword="null"/>.
    /// Throws when more than one hint name matches, so that a test does not read the wrong file.
    /// </summary>
    public string? GetGeneratedSource(string hintNameContains)
    {
        GeneratedSourceResult[] matches = [.. GeneratedSources.Where(s => s.HintName.Contains(hintNameContains, StringComparison.Ordinal))];
        return matches.Length switch
        {
            0 => null,
            1 => matches[0].SourceText.ToString(),
            _ => throw new InvalidOperationException($"More than one generated source matches '{hintNameContains}': {string.Join(", ", matches.Select(static m => m.HintName))}"),
        };
    }

    /// <summary>The diagnostics that are not suppressed.</summary>
    public IEnumerable<Diagnostic> ReportedDiagnostics => Diagnostics.Where(static d => !d.IsSuppressed);

    /// <summary>The only reported diagnostic with <paramref name="id"/>.</summary>
    public Diagnostic GetDiagnostic(string id) => ReportedDiagnostics.Single(d => d.Id == id);

    /// <summary>
    /// Verifies that the output compiles without errors, that the generated code has no warnings when nullable warnings
    /// are enabled, and that the generated code follows <see cref="GeneratedCodeRules"/>.
    /// </summary>
    public async Task VerifyCompilableAsync()
    {
        Diagnostic[] errors = [.. OutputCompilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error)];
        await Assert.That(errors).IsEmpty().Because($"The output has compile errors:{Environment.NewLine}{Describe(errors)}{Environment.NewLine}{DumpGeneratedSources()}");

        IReadOnlyList<string> problems = GeneratedCodeRules.Check(OutputCompilation, GeneratedTrees());
        await Assert.That(problems).IsEmpty().Because($"The generated code breaks the rules:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");

        Diagnostic[] warnings = GetWarningsWithNullableEnabled();
        await Assert.That(warnings).IsEmpty().Because($"The generated files have warnings when they are compiled with nullable warnings enabled:{Environment.NewLine}{Describe(warnings)}");
    }

    /// <summary>Verifies that the output has at least one compile error.</summary>
    public async Task VerifyHasCompilationErrorsAsync()
    {
        IEnumerable<Diagnostic> errors = OutputCompilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(errors).IsNotEmpty().Because("The output compiles, but compile errors were expected.");
    }

    /// <summary>The syntax trees that the generator added to the output.</summary>
    public IReadOnlyList<SyntaxTree> GeneratedTrees() => [.. GeneratedSources.Select(static s => s.SyntaxTree)];

    /// <summary>
    /// Compiles the generated files again with <c>#nullable enable warnings</c> and returns all warnings in them, nullable
    /// or not. Generated files disable nullable warnings because nullable analysis dominates their compile time, so this
    /// check keeps them nullable-correct.
    /// </summary>
    private Diagnostic[] GetWarningsWithNullableEnabled()
    {
        Compilation compilation = OutputCompilation;
        HashSet<SyntaxTree> checkedTrees = [];
        foreach(SyntaxTree tree in GeneratedTrees())
        {
            string text = tree.GetText().ToString().Replace("#nullable disable warnings", "#nullable enable warnings", StringComparison.Ordinal);
            SyntaxTree replaced = tree.WithChangedText(SourceText.From(text));
            compilation = compilation.ReplaceSyntaxTree(tree, replaced);
            checkedTrees.Add(replaced);
        }

        return [.. compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Warning && d.Location.SourceTree is { } t && checkedTrees.Contains(t))];
    }

    private string DumpGeneratedSources() => string.Join(
        Environment.NewLine,
        GeneratedSources.Select(static s => $"// File: {s.HintName}{Environment.NewLine}{AddLineNumbers(s.SourceText.ToString())}"));

    private static string Describe(IEnumerable<Diagnostic> diagnostics) => string.Join(Environment.NewLine, diagnostics);

    private static string AddLineNumbers(string source)
    {
        string[] lines = source.Split('\n');
        int width = lines.Length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        return string.Join(Environment.NewLine, lines.Select((line, i) => $"{(i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width)} | {line.TrimEnd('\r')}"));
    }
}
