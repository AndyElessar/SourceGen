namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Combined container and grouped registrations for pipeline caching.
/// </summary>
/// <param name="Container">The container model.</param>
/// <param name="Groups">The pre-computed registration groups.</param>
internal sealed record ContainerWithGroups(
    ContainerModel Container,
    ContainerRegistrationGroups Groups);

/// <summary>
/// Helper record to group registrations for container generation.
/// Uses immutable collections for proper incremental generator caching.
/// </summary>
/// <param name="ByServiceTypeAndKey">Registrations grouped by service type and key for efficient lookup.</param>
/// <param name="AllServiceTypes">All unique service type names for IsService checks.</param>
/// <param name="Singletons">Singleton registrations with pre-computed names.</param>
/// <param name="Scoped">Scoped registrations with pre-computed names.</param>
/// <param name="Transients">Transient registrations with pre-computed names.</param>
/// <param name="HasOpenGenerics">Whether there are any open generic registrations.</param>
/// <param name="HasKeyedServices">Whether there are any keyed service registrations.</param>
/// <param name="CollectionServiceTypes">Service types with multiple implementations for IEnumerable resolution.</param>
/// <param name="CollectionRegistrations">Pre-computed registrations for each collection service type.</param>
/// <param name="ReversedSingletonsForDisposal">Singletons in reverse order for disposal (excluding open generics).</param>
/// <param name="ReversedScopedForDisposal">Scoped services in reverse order for disposal (excluding open generics).</param>
internal sealed record ContainerRegistrationGroups(
    ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> ByServiceTypeAndKey,
    ImmutableEquatableSet<string> AllServiceTypes,
    ImmutableEquatableArray<CachedRegistration> Singletons,
    ImmutableEquatableArray<CachedRegistration> Scoped,
    ImmutableEquatableArray<CachedRegistration> Transients,
    bool HasOpenGenerics,
    bool HasKeyedServices,
    ImmutableEquatableArray<string> CollectionServiceTypes,
    ImmutableEquatableDictionary<string, ImmutableEquatableArray<CachedRegistration>> CollectionRegistrations,
    ImmutableEquatableArray<CachedRegistration> ReversedSingletonsForDisposal,
    ImmutableEquatableArray<CachedRegistration> ReversedScopedForDisposal);

/// <summary>
/// A registration with pre-computed field and method names for efficient code generation.
/// </summary>
/// <param name="Registration">The original service registration model.</param>
/// <param name="FieldName">The pre-computed field name for storing the service instance.</param>
/// <param name="ResolverMethodName">The pre-computed resolver method name.</param>
internal readonly record struct CachedRegistration(
    ServiceRegistrationModel Registration,
    string FieldName,
    string ResolverMethodName);
