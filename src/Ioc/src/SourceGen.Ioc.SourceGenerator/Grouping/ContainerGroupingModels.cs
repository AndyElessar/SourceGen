using ContainerEntryModel = SourceGen.Ioc.IocSourceGenerator.ContainerEntry;
using FuncWrapperEntryModel = SourceGen.Ioc.IocSourceGenerator.FuncWrapperContainerEntry;
using KvpWrapperEntryModel = SourceGen.Ioc.IocSourceGenerator.KvpWrapperContainerEntry;
using LazyWrapperEntryModel = SourceGen.Ioc.IocSourceGenerator.LazyWrapperContainerEntry;

namespace SourceGen.Ioc.SourceGenerator.Models;

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
internal sealed record ContainerRegistrationGroups(
    ImmutableEquatableDictionary<(string ServiceType, string? Key), ContainerEntryModel> LastWinsByServiceType,
    ImmutableEquatableSet<string> AllServiceTypes,
    bool HasOpenGenerics,
    bool HasKeyedServices,
    ImmutableEquatableArray<string> CollectionServiceTypes,
    ImmutableEquatableArray<ContainerEntryModel> SingletonEntries,
    ImmutableEquatableArray<ContainerEntryModel> ScopedEntries,
    ImmutableEquatableArray<ContainerEntryModel> TransientEntries,
    ImmutableEquatableArray<LazyWrapperEntryModel> LazyWrapperEntries,
    ImmutableEquatableArray<FuncWrapperEntryModel> FuncWrapperEntries,
    ImmutableEquatableArray<KvpWrapperEntryModel> KvpWrapperEntries,
    ImmutableEquatableArray<ContainerEntryModel> CollectionEntries);