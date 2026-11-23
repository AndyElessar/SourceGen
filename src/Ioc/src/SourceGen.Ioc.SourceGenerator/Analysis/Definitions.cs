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
        foreach(IGrouping<NamedTypeRef, RegistrationDeclaration> group in declarations.GroupBy(static d => d.Implementation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RegistrationDeclaration first = group.First();

            // A merge with another lifetime is SGIOC001, which a later slice reports. Until then the definition is skipped.
            if(group.Any(d => d.Lifetime != first.Lifetime))
            {
                continue;
            }

            TypeRef[] others = [.. group
                .SelectMany(static d => d.ServiceTypes)
                .Where(t => !t.Equals(first.Implementation))
                .Distinct()
                .OrderBy(static t => t.SortName, StringComparer.Ordinal)
                .ThenBy(static t => t.CanonicalString, StringComparer.Ordinal)];

            definitions.Add(new ClosedDefinition(first.Implementation, first.Lifetime, [first.Implementation, .. others], first.ConstructorParameters));
        }

        return definitions
            .OrderBy(static d => d.Implementation.SortName, StringComparer.Ordinal)
            .ThenBy(static d => d.Implementation.CanonicalString, StringComparer.Ordinal)
            .ToImmutableEquatableArray();
    }
}
