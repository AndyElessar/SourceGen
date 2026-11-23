namespace SourceGen.Ioc;

/// <summary>
/// The tracking names of the pipeline stages (Analysis.Model.spec, "Incremental pipeline"). The incremental tests check
/// the reasons of these steps and look for symbols in their outputs.
/// </summary>
internal static class TrackingNames
{
    /// <summary>The declarations and type summaries that each attribute gives.</summary>
    public const string Declarations = nameof(Declarations);

    /// <summary>The demands of the resolve calls.</summary>
    public const string ResolveCalls = nameof(ResolveCalls);

    /// <summary>The imported instance definitions and demands.</summary>
    public const string Imports = nameof(Imports);

    /// <summary>The instance definitions and demands of each view.</summary>
    public const string Views = nameof(Views);

    /// <summary>The construction plans and the diagnostic data of each view.</summary>
    public const string Analysis = nameof(Analysis);
}
