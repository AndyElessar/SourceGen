namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents the result of processing a single registration, used as intermediate data
/// between pipeline stages. This enables caching at the individual registration level.
/// </summary>
/// <param name="ServiceRegistrations">The service registration models generated from this registration.</param>
/// <param name="Tags">The tags associated with this registration.</param>
/// <param name="OpenGenericEntries">Open generic service type entries for indexing (if this is an open generic registration).</param>
/// <param name="ClosedGenericDependencies">Closed generic dependencies found in constructor parameters.</param>
internal sealed record class BasicRegistrationResult(
    ImmutableEquatableArray<ServiceRegistrationModel> ServiceRegistrations,
    ImmutableEquatableArray<string> Tags,
    ImmutableEquatableArray<OpenGenericEntry> OpenGenericEntries,
    ImmutableEquatableArray<ClosedGenericDependency> ClosedGenericDependencies);

/// <summary>
/// Represents an open generic service type entry for indexing.
/// </summary>
/// <param name="ServiceTypeKey">The service type's NameWithoutGeneric (e.g., "global::Namespace.IRequestHandler").</param>
/// <param name="RegistrationInfo">The registration information for closed generic resolution.</param>
internal readonly record struct OpenGenericEntry(
    string ServiceTypeKey,
    OpenGenericRegistrationInfo RegistrationInfo);

/// <summary>
/// Holds information about an open generic registration for closed generic resolution.
/// </summary>
/// <param name="ImplementationType">The open generic implementation type.</param>
/// <param name="ServiceTypes">All service types this implementation is registered for.</param>
/// <param name="AllInterfaces">All interfaces implemented by the implementation type.</param>
/// <param name="Lifetime">The service lifetime.</param>
/// <param name="Key">The registration key.</param>
/// <param name="KeyType">The key type.</param>
/// <param name="Decorators">The decorators to apply.</param>
/// <param name="Tags">The tags for method grouping.</param>
/// <param name="InjectionMembers">The injection members.</param>
/// <param name="Factory">The factory method data to use for creating instances.</param>
/// <param name="Instance">The static instance path to use for singleton registration.</param>
internal sealed record class OpenGenericRegistrationInfo(
    TypeData ImplementationType,
    ImmutableEquatableArray<TypeData> ServiceTypes,
    ImmutableEquatableArray<TypeData> AllInterfaces,
    ServiceLifetime Lifetime,
    string? Key,
    int KeyType,
    ImmutableEquatableArray<TypeData> Decorators,
    ImmutableEquatableArray<string> Tags,
    ImmutableEquatableArray<InjectionMemberData> InjectionMembers,
    FactoryMethodData? Factory,
    string? Instance);

/// <summary>
/// Represents a closed generic dependency found in a constructor parameter.
/// </summary>
/// <param name="ClosedTypeName">The full name of the closed generic type.</param>
/// <param name="ClosedType">The closed generic type data.</param>
/// <param name="OpenGenericKey">The NameWithoutGeneric for looking up the open generic registration.</param>
internal readonly record struct ClosedGenericDependency(
    string ClosedTypeName,
    TypeData ClosedType,
    string OpenGenericKey);
