using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Parse;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Analysis;

/// <summary>Merges declarations into closed definitions and orders them (Analysis.Model.spec, "Merging" and "Ordering").</summary>
internal static class Definitions
{
    /// <summary>Merges the declarations that have the same identity and sorts the result.</summary>
    /// <param name="declarations">The declarations of the assembly view.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The closed definitions in their ordering.</returns>
    public static ImmutableEquatableArray<ClosedDefinition> Merge(IEnumerable<RegistrationDeclaration> declarations, CancellationToken cancellationToken)
    {
        List<ClosedDefinition> definitions = [];
        foreach(IGrouping<(NamedTypeRef, KeyRef?), RegistrationDeclaration> group in declarations.GroupBy(static d => (d.Implementation, d.Key)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RegistrationDeclaration first = group.First();

            // An invalid declaration, or a merge with another lifetime (SGIOC001, see CheckMerge), skips the complete
            // definition. The error nodes of the dependency graph come with the container.
            if(group.Any(d => d.IsError || d.Lifetime != first.Lifetime))
            {
                continue;
            }

            TypeRef[] others = [.. group
                .SelectMany(static d => d.ServiceTypes)
                .Where(t => !t.Equals(first.Implementation))
                .Distinct()
                .OrderBy(static t => t.SortName, StringComparer.Ordinal)
                .ThenBy(static t => t.CanonicalString, StringComparer.Ordinal)];

            definitions.Add(new ClosedDefinition(first.Implementation, first.Key, first.Lifetime, [first.Implementation, .. others], first.ConstructorParameters, first.Disposal));
        }

        return definitions
            .OrderBy(static d => d.Implementation.SortName, StringComparer.Ordinal)
            .ThenBy(static d => KeyRef.CanonicalOf(d.Key), StringComparer.Ordinal)
            .ThenBy(static d => d.Implementation.CanonicalString, StringComparer.Ordinal)
            .ToImmutableEquatableArray();
    }

    /// <summary>Reports SGIOC001 on each attribute of a definition whose declarations do not agree.</summary>
    /// <param name="sites">The sites of the valid declarations of the view.</param>
    /// <returns>The diagnostics.</returns>
    public static IEnumerable<DiagnosticInfo> CheckMerge(IEnumerable<MergeSite> sites)
    {
        foreach(IGrouping<(NamedTypeRef, KeyRef?), MergeSite> group in sites.GroupBy(static s => (s.Implementation, s.Key)))
        {
            Lifetime lifetime = group.First().Lifetime;
            if(group.All(s => s.Lifetime == lifetime))
            {
                continue;
            }

            foreach(MergeSite site in group)
            {
                yield return new DiagnosticInfo(Descriptors.InconsistentRegistration.Id, [site.Name, "Lifetime"], site.FilePath, site.Span);
            }
        }
    }
}
