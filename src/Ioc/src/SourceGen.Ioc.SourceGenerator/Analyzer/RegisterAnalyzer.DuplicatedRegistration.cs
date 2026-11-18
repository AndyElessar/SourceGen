namespace SourceGen.Ioc;

/// <summary>
/// Partial class for duplicated registration analysis (SGIOC011). SGIOC012 is reported while collecting the default settings.
/// </summary>
public sealed partial class RegisterAnalyzer
{
    /// <summary>
    /// SGIOC011: Analyzes for duplicated registrations (same implementation type, key, and at least one matching tag).
    /// Reports warning when registrations share the same (ImplementationType, Key) and have at least one overlapping tag.
    /// Implementation types are compared as runtime types, without tuple element names. Services without tags use an
    /// empty string tag for comparison.
    /// </summary>
    /// <param name="tags">
    /// The effective tags of the registration: its own, otherwise those of its best default settings, like the generator.
    /// </param>
    private static void AnalyzeDuplicatedRegistration(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        INamedTypeSymbol targetType,
        string? key,
        IEnumerable<string> tags,
        Location? location)
    {
        // Build effective tags list using helper method
        var effectiveTags = AnalyzerHelpers.GetEffectiveTags(tags);

        // Record every effective tag, so a later registration that duplicates this one on another tag is reported too; the
        // registration itself is reported once
        var hasDuplicate = false;
        foreach (var tag in effectiveTags)
            hasDuplicate |= !analyzerContext.RegistrationKeys.Add((targetType, key, tag));

        if (hasDuplicate)
        {
            var keyPart = key is not null ? $" with key '{key}'" : "";
            reportDiagnostic(Diagnostic.Create(
                DuplicatedRegistration,
                location,
                targetType.Name,
                keyPart));
        }
    }
}
