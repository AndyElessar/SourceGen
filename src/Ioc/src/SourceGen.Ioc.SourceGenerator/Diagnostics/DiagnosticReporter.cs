namespace SourceGen.Ioc.Diagnostics;

/// <summary>
/// The output step of the diagnostic pipeline. It finds the syntax tree of each diagnostic by its path in the compilation
/// and creates the location on that tree, so that <c>#pragma</c>, <c>[SuppressMessage]</c>, and <c>.editorconfig</c>
/// apply (Diagnostics.spec, "Two pipelines").
/// </summary>
internal static class DiagnosticReporter
{
    /// <summary>Reports <paramref name="diagnostics"/> with the descriptors of <see cref="Descriptors"/>.</summary>
    /// <param name="context">The output context.</param>
    /// <param name="compilation">The current compilation.</param>
    /// <param name="diagnostics">The diagnostics.</param>
    public static void Report(SourceProductionContext context, Compilation compilation, IEnumerable<DiagnosticInfo> diagnostics)
        => Report(context, compilation, diagnostics, Descriptors.ById);

    /// <summary>Reports <paramref name="diagnostics"/>.</summary>
    /// <param name="context">The output context.</param>
    /// <param name="compilation">The current compilation.</param>
    /// <param name="diagnostics">The diagnostics.</param>
    /// <param name="descriptors">The descriptors by ID.</param>
    public static void Report(
        SourceProductionContext context,
        Compilation compilation,
        IEnumerable<DiagnosticInfo> diagnostics,
        IReadOnlyDictionary<string, DiagnosticDescriptor> descriptors)
    {
        // A path that more than one tree has maps to null: the tree is not known, so the diagnostic has no location
        Dictionary<string, SyntaxTree?>? trees = null;
        foreach(DiagnosticInfo info in diagnostics)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            DiagnosticDescriptor descriptor = descriptors[info.Id];
            Location location = Location.None;
            if(info.FilePath is not null)
            {
                trees ??= compilation.SyntaxTrees
                    .GroupBy(static t => t.FilePath)
                    .ToDictionary(static g => g.Key, static g => g.Skip(1).Any() ? null : g.First());
                if(trees.TryGetValue(info.FilePath, out SyntaxTree? tree) && tree is not null && info.Span.End <= tree.Length)
                {
                    location = Location.Create(tree, info.Span);
                }
            }

            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, [.. info.Arguments]));
        }
    }
}
