using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Partial class for duplicated registration analysis (SGIOC011, SGIOC012).
/// </summary>
public sealed partial class RegisterAnalyzer
{
    /// <summary>
    /// SGIOC012: Analyzes IoCRegisterDefaultsAttribute on types (class, struct, interface).
    /// Reports warning when the same target type with at least one matching tag has multiple default settings.
    /// Services without tags use an empty string tag for comparison.
    /// </summary>
    private static void AnalyzeTypeLevelDefaultsAttribute(SymbolAnalysisContext context, AnalyzerContext analyzerContext)
    {
        if (context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        // Skip if no IoCRegisterDefaultsAttribute is available
        if (analyzerContext.AttributeSymbols.IocRegisterDefaultsAttribute is null && analyzerContext.AttributeSymbols.IocRegisterDefaultsAttribute_T1 is null)
            return;

        foreach (var attribute in typeSymbol.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null)
                continue;

            // Check if this is an IoCRegisterDefaultsAttribute (non-generic or generic)
            if (!AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attributeClass, analyzerContext.AttributeSymbols))
            {
                continue;
            }

            // Extract default settings to get target type name and tags
            var settings = attributeClass.IsGenericType
                ? attribute.ExtractDefaultSettingsFromGenericAttribute()
                : attribute.ExtractDefaultSettings();

            if (settings is null)
                continue;

            var targetTypeName = settings.TargetServiceType.Name;
            var tags = settings.Tags;
            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation();

            // Build effective tags list using helper method
            var effectiveTags = AnalyzerHelpers.GetEffectiveTags(tags);

            // SGIOC012: Check each effective tag for duplicates (shared with assembly-level)
            var hasDuplicate = false;
            foreach (var tag in effectiveTags)
            {
                var defaultKey = (targetTypeName, tag);
                if (!analyzerContext.SeenDefaultTargetTypes.TryAdd(defaultKey, location))
                {
                    hasDuplicate = true;
                    break; // Only need to find one duplicate
                }
            }

            if (hasDuplicate)
            {
                // Report immediately for type-level attributes
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicatedDefaultSettings,
                    location,
                    targetTypeName));
            }
        }
    }

    /// <summary>
    /// SGIOC011: Analyzes for duplicated registrations (same implementation type, key, and at least one matching tag).
    /// Reports warning when registrations share the same (ImplementationType, Key) and have at least one overlapping tag.
    /// Services without tags use an empty string tag for comparison.
    /// </summary>
    private static void AnalyzeDuplicatedRegistration(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        AttributeData attribute,
        INamedTypeSymbol targetType,
        string fullyQualifiedTypeName,
        Location? location)
    {
        // Get the registration key and tags from the attribute
        var (key, _) = attribute.GetKey();
        var tags = attribute.GetTags();

        // Build effective tags list using helper method
        var effectiveTags = AnalyzerHelpers.GetEffectiveTags(tags);

        // Check each effective tag for duplicates
        var hasDuplicate = false;
        foreach (var tag in effectiveTags)
        {
            var registrationKey = (fullyQualifiedTypeName, key, tag);

            // Try to add; if already exists, mark as duplicate
            if (!analyzerContext.RegistrationKeys.TryAdd(registrationKey, location))
            {
                hasDuplicate = true;
                break; // Only need to find one duplicate
            }
        }

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
