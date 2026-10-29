namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Combined container and grouped registrations for pipeline caching.
    /// </summary>
    /// <param name="Container">The container model.</param>
    /// <param name="Groups">The pre-computed registration groups.</param>
    private sealed record class ContainerWithGroups(
        ContainerModel Container,
        ContainerRegistrationGroups Groups);

    /// <summary>
    /// Helper record to group registrations for container generation.
    /// Uses immutable collections for proper incremental generator caching.
    /// </summary>
    /// <param name="LastWinsByServiceType">Last registration wins lookup by service type and key using container entry models.</param>
    /// <param name="AllServiceTypes">All unique service type names for IsService checks.</param>
    /// <param name="HasOpenGenerics">Whether there are any open generic registrations.</param>
    /// <param name="HasKeyedServices">Whether there are any keyed service registrations.</param>
    /// <param name="CollectionServiceTypes">Service types with multiple implementations for IEnumerable resolution.</param>
    /// <param name="SingletonEntries">Parallel service entry model for singleton registrations.</param>
    /// <param name="ScopedEntries">Parallel service entry model for scoped registrations.</param>
    /// <param name="TransientEntries">Parallel service entry model for transient registrations.</param>
    /// <param name="LazyWrapperEntries">Wrapper entry model for <c>Lazy&lt;T&gt;</c> registrations.</param>
    /// <param name="FuncWrapperEntries">Wrapper entry model for <c>Func&lt;T&gt;</c> registrations.</param>
    /// <param name="KvpWrapperEntries">Wrapper entry model for <c>KeyValuePair&lt;TKey, TValue&gt;</c> registrations.</param>
    /// <param name="CollectionEntries">Parallel collection entry model for IEnumerable and related wrappers.</param>
    private sealed record class ContainerRegistrationGroups(
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ContainerEntry> LastWinsByServiceType,
        ImmutableEquatableSet<string> AllServiceTypes,
        bool HasOpenGenerics,
        bool HasKeyedServices,
        ImmutableEquatableArray<string> CollectionServiceTypes,
        ImmutableEquatableArray<ContainerEntry> SingletonEntries,
        ImmutableEquatableArray<ContainerEntry> ScopedEntries,
        ImmutableEquatableArray<ContainerEntry> TransientEntries,
        ImmutableEquatableArray<LazyWrapperContainerEntry> LazyWrapperEntries,
        ImmutableEquatableArray<FuncWrapperContainerEntry> FuncWrapperEntries,
        ImmutableEquatableArray<KvpWrapperContainerEntry> KvpWrapperEntries,
        ImmutableEquatableArray<ContainerEntry> CollectionEntries);
}
