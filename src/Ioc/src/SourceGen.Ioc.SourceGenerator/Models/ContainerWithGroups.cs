using ContainerEntryModel = SourceGen.Ioc.IocSourceGenerator.ContainerEntry;

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
/// <param name="LazyEntries">Pre-computed Lazy wrapper entries for container code generation.</param>
/// <param name="FuncEntries">Pre-computed Func wrapper entries for container code generation.</param>
/// <param name="KvpEntries">Pre-computed KeyValuePair entries for container code generation.</param>
/// <param name="HasOpenGenerics">Whether there are any open generic registrations.</param>
/// <param name="HasKeyedServices">Whether there are any keyed service registrations.</param>
/// <param name="CollectionServiceTypes">Service types with multiple implementations for IEnumerable resolution.</param>
/// <param name="SingletonEntries">Parallel service entry model for singleton registrations.</param>
/// <param name="ScopedEntries">Parallel service entry model for scoped registrations.</param>
/// <param name="TransientEntries">Parallel service entry model for transient registrations.</param>
/// <param name="WrapperEntries">Parallel wrapper entry model for Lazy/Func/KVP registrations.</param>
/// <param name="CollectionEntries">Parallel collection entry model for IEnumerable and related wrappers.</param>
internal sealed record ContainerRegistrationGroups(
    ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> ByServiceTypeAndKey,
    ImmutableEquatableSet<string> AllServiceTypes,
    ImmutableEquatableArray<ContainerLazyEntry> LazyEntries,
    ImmutableEquatableArray<ContainerFuncEntry> FuncEntries,
    ImmutableEquatableArray<ContainerKvpEntry> KvpEntries,
    bool HasOpenGenerics,
    bool HasKeyedServices,
    ImmutableEquatableArray<string> CollectionServiceTypes,
    ImmutableEquatableArray<ContainerEntryModel> SingletonEntries,
    ImmutableEquatableArray<ContainerEntryModel> ScopedEntries,
    ImmutableEquatableArray<ContainerEntryModel> TransientEntries,
    ImmutableEquatableArray<ContainerEntryModel> WrapperEntries,
    ImmutableEquatableArray<ContainerEntryModel> CollectionEntries);

/// <summary>
/// A registration with pre-computed field and method names for efficient code generation.
/// </summary>
/// <param name="Registration">The original service registration model.</param>
/// <param name="FieldName">The pre-computed field name for storing the service instance.</param>
/// <param name="ResolverMethodName">The pre-computed resolver method name.</param>
/// <param name="IsEager">Whether this registration should be eagerly resolved during container/scope construction.</param>
/// <param name="IsAsyncInit">Whether this registration has async initialization members (pre-computed from <see cref="ServiceRegistrationModel.InjectionMembers"/>).</param>
internal readonly record struct CachedRegistration(
    ServiceRegistrationModel Registration,
    string FieldName,
    string ResolverMethodName,
    bool IsEager,
    bool IsAsyncInit);

/// <summary>
/// Represents a Lazy resolver entry for container code generation.
/// </summary>
/// <param name="InnerServiceTypeName">The fully-qualified inner service type name.</param>
/// <param name="ResolverMethodName">The method name of the inner service resolver.</param>
/// <param name="FieldName">The field name for storing the wrapper instance.</param>
internal readonly record struct ContainerLazyEntry(
    string InnerServiceTypeName,
    string ResolverMethodName,
    string FieldName);

/// <summary>
/// Represents a Func resolver entry for container code generation.
/// </summary>
/// <param name="InnerServiceTypeName">The fully-qualified inner service type name.</param>
/// <param name="ResolverMethodName">The method name of the inner service resolver.</param>
/// <param name="FieldName">The field name for storing the wrapper instance.</param>
internal readonly record struct ContainerFuncEntry(
    string InnerServiceTypeName,
    string ResolverMethodName,
    string FieldName);

/// <summary>
/// Represents a KeyValuePair resolver entry for container code generation.
/// </summary>
/// <param name="KeyTypeName">The fully-qualified key type name.</param>
/// <param name="ValueTypeName">The fully-qualified value type name.</param>
/// <param name="KeyExpr">The key literal expression.</param>
/// <param name="ResolverMethodName">The method name of the value service resolver.</param>
/// <param name="KvpResolverMethodName">The method name for this KVP resolver.</param>
internal readonly record struct ContainerKvpEntry(
    string KeyTypeName,
    string ValueTypeName,
    string KeyExpr,
    string ResolverMethodName,
    string KvpResolverMethodName);
