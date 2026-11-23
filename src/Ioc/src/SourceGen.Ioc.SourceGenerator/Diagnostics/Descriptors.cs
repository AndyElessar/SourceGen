namespace SourceGen.Ioc.Diagnostics;

/// <summary>
/// The descriptors of the IoC diagnostics (Diagnostics.spec). Each slice of the rewrite adds the descriptors that it
/// reports, together with a row in <c>AnalyzerReleases.Unshipped.md</c>.
/// </summary>
internal static class Descriptors
{
    /// <summary>The category of every descriptor.</summary>
    public const string Category = "SourceGen.Ioc";

    /// <summary>SGIOC108: the <c>Add{Name}</c> method has no valid name.</summary>
    public static readonly DiagnosticDescriptor InvalidAddMethodName = Create(
        "SGIOC108",
        "Invalid Add method name",
        "'{0}' does not give a valid name for the Add method: {1}",
        DiagnosticSeverity.Error);

    /// <summary>Gets the descriptors by ID.</summary>
    public static IReadOnlyDictionary<string, DiagnosticDescriptor> ById { get; } = new[]
    {
        InvalidAddMethodName,
    }.ToDictionary(static d => d.Id);

    /// <summary>
    /// Creates a descriptor by the rules of Diagnostics.spec, "Descriptors": an Error has
    /// <see cref="WellKnownDiagnosticTags.NotConfigurable"/>, so that configuration cannot lower or disable it. A Warning
    /// or an Info does not have it, so that <c>#pragma</c> and the configuration work.
    /// </summary>
    /// <param name="id">The diagnostic ID.</param>
    /// <param name="title">The title.</param>
    /// <param name="messageFormat">The message format.</param>
    /// <param name="severity">The severity.</param>
    /// <returns>The descriptor.</returns>
    internal static DiagnosticDescriptor Create(string id, string title, string messageFormat, DiagnosticSeverity severity)
        => new(
            id,
            title,
            messageFormat,
            Category,
            severity,
            isEnabledByDefault: true,
            customTags: severity == DiagnosticSeverity.Error ? [WellKnownDiagnosticTags.NotConfigurable] : []);
}
