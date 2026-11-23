using Microsoft.CodeAnalysis.Text;

namespace SourceGen.Ioc.Diagnostics;

/// <summary>
/// A diagnostic in the model: the ID, the message arguments, the file path, and the span. It holds no
/// <see cref="Location"/>, so an edit that only moves code does not run the analysis again
/// (Diagnostics.spec, "Two pipelines").
/// </summary>
/// <param name="Id">The diagnostic ID.</param>
/// <param name="Arguments">The message arguments, already formatted.</param>
/// <param name="FilePath">The path of the syntax tree, or <see langword="null"/> when the diagnostic has no location.</param>
/// <param name="Span">The span in the syntax tree.</param>
internal sealed record DiagnosticInfo(string Id, ImmutableEquatableArray<string> Arguments, string? FilePath, TextSpan Span)
{
    /// <summary>Creates a diagnostic at <paramref name="location"/>.</summary>
    /// <param name="id">The diagnostic ID.</param>
    /// <param name="location">The location. Only a location in a syntax tree keeps its file and span.</param>
    /// <param name="arguments">The message arguments.</param>
    /// <returns>The diagnostic.</returns>
    public static DiagnosticInfo Create(string id, Location? location, params string[] arguments)
        => location?.SourceTree is { } tree
            ? new(id, arguments.ToImmutableEquatableArray(), tree.FilePath, location.SourceSpan)
            : new(id, arguments.ToImmutableEquatableArray(), null, default);

    /// <summary>Creates a diagnostic on <paramref name="node"/>.</summary>
    /// <param name="id">The diagnostic ID.</param>
    /// <param name="node">The syntax node.</param>
    /// <param name="arguments">The message arguments.</param>
    /// <returns>The diagnostic.</returns>
    public static DiagnosticInfo Create(string id, SyntaxNode node, params string[] arguments)
        => new(id, arguments.ToImmutableEquatableArray(), node.SyntaxTree.FilePath, node.Span);
}
