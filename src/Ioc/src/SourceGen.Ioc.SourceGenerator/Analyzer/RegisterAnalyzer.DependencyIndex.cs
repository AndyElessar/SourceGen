namespace SourceGen.Ioc;

/// <summary>
/// Partial class for the lookup of the registrations a dependency resolves to (SGIOC002-005, SGIOC033).
/// </summary>
public sealed partial class RegisterAnalyzer
{
    /// <summary>
    /// The position of a registration in the generator's registration order: its pipeline (see
    /// <see cref="AnalyzerHelpers.GetGeneratorRegistrationPipeline"/>), then its source position.
    /// </summary>
    private readonly record struct GeneratorRegistrationOrder(int Pipeline, (int Tree, int Position) Source)
        : IComparable<GeneratorRegistrationOrder>
    {
        public int CompareTo(GeneratorRegistrationOrder other)
        {
            var byPipeline = Pipeline.CompareTo(other.Pipeline);
            return byPipeline != 0 ? byPipeline : Source.CompareTo(other.Source);
        }
    }

    /// <summary>
    /// A registration a dependency can resolve to: its implementation type and its own lifetime and activation.
    /// </summary>
    private readonly record struct RegisteredDependency(ServiceInfo Info, ServiceActivation Activation)
    {
        public INamedTypeSymbol Type => Info.Type;

        public ServiceLifetime Lifetime => Activation.Lifetime;
    }

    /// <summary>
    /// A registration with the (service type, key) pairs it registers and its effective tags (<c>""</c> when it has none).
    /// </summary>
    private sealed record DependencyRegistration(
        RegisteredDependency Dependency,
        ImmutableArray<ServiceSymbolKey> Registrations,
        ImmutableArray<string> Tags,
        GeneratorRegistrationOrder Order);

    /// <summary>
    /// The registrations each (service type, key) resolves to, like the generator: the last registration wins. Which
    /// registrations are registered depends on the tags (see Register.Tags.spec.md): the registrations without tags when no
    /// tag is passed, otherwise those with a passed tag. The order differs between the outputs:
    /// <list type="bullet">
    ///   <item>The container output adds the registrations in the generator's registration order, so the last registration of
    ///   a set of tags is the last of the winners of these tags (each the last registration with the tag in that order).</item>
    ///   <item>The Register output adds them grouped by their sorted tags, the groups ordered like the generator's
    ///   <c>TagArrayComparer</c>, each group in the generator's registration order. The last registration of a set of passed
    ///   tags is the last one of the last group with one of them, which is the last registration in this order of a tag of
    ///   that group.</item>
    /// </list>
    /// So a (service type, key) resolves to the winner of each tag (and of the registrations without tags) in either order.
    /// </summary>
    private sealed class DependencyIndex
    {
        private readonly Dictionary<ServiceSymbolKey, ImmutableArray<RegisteredDependency>> _winners;

        private DependencyIndex(Dictionary<ServiceSymbolKey, ImmutableArray<RegisteredDependency>> winners)
            => _winners = winners;

        public static DependencyIndex Build(List<DependencyRegistration> registrations, CancellationToken cancellationToken)
        {
            var entries = new List<IndexedRegistration>(registrations.Count);
            foreach(var registration in registrations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var serviceTypes = new List<ServiceSymbolKey>();
                foreach(var serviceKey in registration.Registrations)
                {
                    foreach(var serviceType in GetIndexedServiceTypes(registration.Dependency.Type, serviceKey.ServiceType))
                        serviceTypes.Add(new(serviceType, serviceKey.Key));
                }

                entries.Add(new(registration, GetRegisterTagGroup(registration.Tags), serviceTypes));
            }

            // OrderBy is stable, so registrations without a source position keep their order
            var builders = new Dictionary<ServiceSymbolKey, ImmutableArray<RegisteredDependency>.Builder>();
            AddWinners(entries.OrderBy(static entry => entry.Registration.Order));
            AddWinners(entries
                .OrderBy(static entry => entry.TagGroup, TagGroupComparer.Instance)
                .ThenBy(static entry => entry.Registration.Order));

            void AddWinners(IEnumerable<IndexedRegistration> ordered)
            {
                var lastByTag = new Dictionary<(ServiceSymbolKey ServiceType, string Tag), RegisteredDependency>();
                foreach(var (registration, _, serviceTypes) in ordered)
                {
                    foreach(var serviceType in serviceTypes)
                    {
                        foreach(var tag in registration.Tags)
                            lastByTag[(serviceType, tag)] = registration.Dependency;
                    }
                }

                foreach(var kvp in lastByTag)
                {
                    if(!builders.TryGetValue(kvp.Key.ServiceType, out var builder))
                    {
                        builder = ImmutableArray.CreateBuilder<RegisteredDependency>(1);
                        builders[kvp.Key.ServiceType] = builder;
                    }

                    // Several tags (and both orders) often pick the same registration
                    if(!builder.Contains(kvp.Value))
                        builder.Add(kvp.Value);
                }
            }

            var winners = new Dictionary<ServiceSymbolKey, ImmutableArray<RegisteredDependency>>(builders.Count);
            foreach(var kvp in builders)
                winners[kvp.Key] = kvp.Value.ToImmutable();

            return new(winners);
        }

        /// <summary>
        /// Gets the tag group of a registration in the Register output: its tags sorted ordinally, empty without tags.
        /// </summary>
        private static ImmutableArray<string> GetRegisterTagGroup(ImmutableArray<string> effectiveTags)
            => effectiveTags is [""] ? [] : [.. effectiveTags.OrderBy(static tag => tag, StringComparer.Ordinal)];

        /// <summary>
        /// Orders tag groups like the generator's <c>GroupRegistrationsForRegister.TagArrayComparer</c>: element by element
        /// (ordinal), then the shorter first. Keep both in sync.
        /// </summary>
        private sealed class TagGroupComparer : IComparer<ImmutableArray<string>>
        {
            public static readonly TagGroupComparer Instance = new();

            public int Compare(ImmutableArray<string> x, ImmutableArray<string> y)
            {
                var minLength = Math.Min(x.Length, y.Length);
                for(var i = 0; i < minLength; i++)
                {
                    var comparison = StringComparer.Ordinal.Compare(x[i], y[i]);
                    if(comparison != 0)
                        return comparison;
                }

                return x.Length.CompareTo(y.Length);
            }
        }

        private readonly record struct IndexedRegistration(
            DependencyRegistration Registration,
            ImmutableArray<string> TagGroup,
            List<ServiceSymbolKey> ServiceTypes);

        /// <summary>
        /// Gets the registrations a dependency on <paramref name="type"/> with <paramref name="key"/> resolves to: those of
        /// the type itself, otherwise, for a closed generic type, those of its open generic definition (like MS.DI, which
        /// prefers a closed registration).
        /// </summary>
        public ImmutableArray<RegisteredDependency> Find(INamedTypeSymbol type, string? key)
        {
            if(_winners.TryGetValue(new(type, key), out var winners))
                return winners;

            if(type.IsGenericType && !type.IsUnboundGenericType && _winners.TryGetValue(new(type.OriginalDefinition, key), out winners))
                return winners;

            return [];
        }

        /// <summary>
        /// Gets the types a registered service type is looked up by: a closed type as itself, an open one by its definition
        /// when the generator registers it open (its type arguments are the implementation's type parameters in declaration
        /// order), and an unbound service type of a closed implementation as each closed form the implementation implements
        /// or inherits (see Register.Basic.spec.md).
        /// </summary>
        private static IEnumerable<INamedTypeSymbol> GetIndexedServiceTypes(INamedTypeSymbol implementation, INamedTypeSymbol serviceType)
        {
            var definition = implementation.IsUnboundGenericType ? implementation.OriginalDefinition : implementation;
            if(!serviceType.IsUnboundGenericType)
            {
                if(GetIndexedType(definition, serviceType) is { } indexed)
                    yield return indexed;

                yield break;
            }

            foreach(var form in AnalyzerHelpers.EnumerateImplicitServiceTypes(definition))
            {
                if(SymbolEqualityComparer.Default.Equals(form.OriginalDefinition, serviceType.OriginalDefinition)
                    && GetIndexedType(definition, form) is { } indexed)
                {
                    yield return indexed;
                }
            }

            static INamedTypeSymbol? GetIndexedType(INamedTypeSymbol definition, INamedTypeSymbol serviceType)
            {
                if(!AnalyzerHelpers.IsOpenGenericType(serviceType))
                    return serviceType;

                return serviceType.TypeArguments.SequenceEqual<ITypeSymbol>(definition.TypeParameters, SymbolEqualityComparer.Default)
                    ? serviceType.OriginalDefinition
                    : null;
            }
        }
    }
}
