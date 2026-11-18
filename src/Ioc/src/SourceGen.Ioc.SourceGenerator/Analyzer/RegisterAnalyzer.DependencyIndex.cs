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
    /// A registration with the (service type, key) pairs it registers and its tags (its own, otherwise those of its best
    /// default settings; empty without tags).
    /// </summary>
    /// <param name="Dependency">The registration, with the activation of its implementation type's self-registration.</param>
    /// <param name="ServiceTypeActivation">
    /// The activation of its other service types, which differs from that of the self-registration when a generic factory's
    /// mapping does not match the implementation type, so the self-registration is constructed.
    /// </param>
    private sealed record DependencyRegistration(
        RegisteredDependency Dependency,
        ServiceActivation ServiceTypeActivation,
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
        private readonly Dictionary<ServiceSymbolKey, Winners> _winners;

        private DependencyIndex(Dictionary<ServiceSymbolKey, Winners> winners)
            => _winners = winners;

        /// <summary>
        /// The winners of a (service type, key): each with the tag it wins (<see langword="null"/> for the registrations
        /// without tags), and the distinct registrations among them.
        /// </summary>
        private sealed record Winners(
            ImmutableArray<(string? Tag, RegisteredDependency Dependency)> ByTag,
            ImmutableArray<RegisteredDependency> Distinct);

        public static DependencyIndex Build(List<DependencyRegistration> registrations, CancellationToken cancellationToken)
        {
            var entries = new List<IndexedRegistration>(registrations.Count);
            foreach(var registration in registrations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var implementation = registration.Dependency.Type;
                var serviceTypes = new List<(ServiceSymbolKey ServiceType, RegisteredDependency Dependency)>();
                foreach(var serviceKey in registration.Registrations)
                {
                    foreach(var serviceType in GetIndexedServiceTypes(implementation, serviceKey.ServiceType))
                    {
                        var dependency = IsSelfRegistration(implementation, serviceType)
                            ? registration.Dependency
                            : registration.Dependency with { Activation = registration.ServiceTypeActivation };
                        serviceTypes.Add((new(serviceType, serviceKey.Key), dependency));
                    }
                }

                entries.Add(new(registration, [.. registration.Tags.OrderBy(static tag => tag, StringComparer.Ordinal)], serviceTypes));
            }

            // OrderBy is stable, so registrations without a source position keep their order
            var byTag = new Dictionary<ServiceSymbolKey, List<(string? Tag, RegisteredDependency Dependency)>>();
            AddWinners(entries.OrderBy(static entry => entry.Registration.Order));
            AddWinners(entries
                .OrderBy(static entry => entry.TagGroup, TagGroupComparer.Instance)
                .ThenBy(static entry => entry.Registration.Order));

            void AddWinners(IEnumerable<IndexedRegistration> ordered)
            {
                var lastByTag = new Dictionary<(ServiceSymbolKey ServiceType, string? Tag), RegisteredDependency>();
                foreach(var (registration, _, serviceTypes) in ordered)
                {
                    foreach(var (serviceType, dependency) in serviceTypes)
                    {
                        if(registration.Tags.IsEmpty)
                        {
                            lastByTag[(serviceType, null)] = dependency;
                            continue;
                        }

                        foreach(var tag in registration.Tags)
                            lastByTag[(serviceType, tag)] = dependency;
                    }
                }

                foreach(var kvp in lastByTag)
                {
                    if(!byTag.TryGetValue(kvp.Key.ServiceType, out var winners))
                    {
                        winners = [];
                        byTag[kvp.Key.ServiceType] = winners;
                    }

                    // Both orders often pick the same registration for a tag
                    if(!winners.Contains((kvp.Key.Tag, kvp.Value)))
                        winners.Add((kvp.Key.Tag, kvp.Value));
                }
            }

            var result = new Dictionary<ServiceSymbolKey, Winners>(byTag.Count);
            foreach(var kvp in byTag)
                result[kvp.Key] = new([.. kvp.Value], Distinct(kvp.Value.Select(static winner => winner.Dependency)));

            return new(result);
        }

        private static ImmutableArray<RegisteredDependency> Distinct(IEnumerable<RegisteredDependency> dependencies)
        {
            var builder = ImmutableArray.CreateBuilder<RegisteredDependency>();
            foreach(var dependency in dependencies)
            {
                // Several tags often pick the same registration
                if(!builder.Contains(dependency))
                    builder.Add(dependency);
            }

            return builder.ToImmutable();
        }

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

        /// <param name="TagGroup">The tag group in the Register output: the tags sorted ordinally, empty without tags.</param>
        private readonly record struct IndexedRegistration(
            DependencyRegistration Registration,
            ImmutableArray<string> TagGroup,
            List<(ServiceSymbolKey ServiceType, RegisteredDependency Dependency)> ServiceTypes);

        /// <summary>
        /// Gets the registrations a dependency on <paramref name="type"/> with <paramref name="key"/> resolves to: per tag
        /// (and for the registrations without tags), those of the type itself, otherwise, for a closed generic type, those of
        /// its open generic definition, like MS.DI, which prefers a closed registration among those registered.
        /// </summary>
        public ImmutableArray<RegisteredDependency> Find(INamedTypeSymbol type, string? key)
        {
            _winners.TryGetValue(new(type, key), out var closed);

            Winners? open = null;
            if(type.IsGenericType && !type.IsUnboundGenericType)
                _winners.TryGetValue(new(type.OriginalDefinition, key), out open);

            if(open is null)
                return closed?.Distinct ?? [];

            if(closed is null)
                return open.Distinct;

            // An open registration still resolves the type for the tags without a closed registration
            var dependencies = new List<RegisteredDependency>(closed.Distinct);
            foreach(var (tag, dependency) in open.ByTag)
            {
                if(!HasTag(closed.ByTag, tag))
                    dependencies.Add(dependency);
            }

            return Distinct(dependencies);
        }

        private static bool HasTag(ImmutableArray<(string? Tag, RegisteredDependency Dependency)> winners, string? tag)
        {
            foreach(var winner in winners)
            {
                if(winner.Tag == tag)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Gets whether an indexed service type is the implementation type itself (its definition for an open one).
        /// </summary>
        private static bool IsSelfRegistration(INamedTypeSymbol implementation, INamedTypeSymbol serviceType)
            => RuntimeTypeSymbolComparer.Instance.Equals(serviceType, implementation)
                || SymbolEqualityComparer.Default.Equals(serviceType, implementation.OriginalDefinition);

        /// <summary>
        /// Gets the types a registered service type is looked up by, like the generator's <c>IsValidServiceType</c>: a closed
        /// type of a closed implementation as itself; an open one of an open implementation by its definition when the
        /// generator registers it open (its type arguments are the implementation's type parameters in declaration order);
        /// an unbound service type of a closed implementation as each closed form the implementation implements or inherits
        /// (see Register.Basic.spec.md). A closed service type of an open implementation is not registered.
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
                    return AnalyzerHelpers.IsOpenGenericType(definition) ? null : serviceType;

                return serviceType.TypeArguments.SequenceEqual<ITypeSymbol>(definition.TypeParameters, SymbolEqualityComparer.Default)
                    ? serviceType.OriginalDefinition
                    : null;
            }
        }
    }
}
