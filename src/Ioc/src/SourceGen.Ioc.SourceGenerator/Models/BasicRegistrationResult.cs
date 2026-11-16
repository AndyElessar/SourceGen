namespace SourceGen.Ioc;

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
/// <param name="KeyType">The key type as declared: 0 for a value, 1 for a key given as C# code (<c>KeyType.Csharp</c>).</param>
/// <param name="KeyValueType">The type data of the key value, or null if unknown.</param>
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
    TypeData? KeyValueType,
    ImmutableEquatableArray<TypeData> Decorators,
    ImmutableEquatableArray<string> Tags,
    ImmutableEquatableArray<InjectionMemberData> InjectionMembers,
    FactoryMethodData? Factory,
    string? Instance)
{
    /// <summary>Whether <see cref="Key"/> is given as C# code (<c>KeyType.Csharp</c>), which may equal any key at runtime.</summary>
    public bool IsCsharpKey => Key is not null && KeyType == 1;
}

/// <summary>
/// How a closed generic dependency is resolved with a key, which decides the open generic registrations it closes.
/// </summary>
internal enum DependencyKeyKind : byte
{
    /// <summary>
    /// Resolved with <see cref="ClosedGenericDependency.Key"/>, a constant value, or without a key when it is
    /// <see langword="null"/>.
    /// </summary>
    Value,

    /// <summary>
    /// Resolved with <see cref="ClosedGenericDependency.Key"/>, given as C# code (<c>KeyType.Csharp</c>), which may equal
    /// any key at runtime.
    /// </summary>
    Csharp,

    /// <summary>
    /// Resolved with a key unknown at compile time, or without a key (<c>[IocDiscover]</c>): closes the first open generic
    /// registration that matches, with its key.
    /// </summary>
    Unknown,

    /// <summary>
    /// Resolved with a key unknown at compile time (keyed <c>IServiceProvider</c> invocations): closes the first keyed open
    /// generic registration that matches, otherwise the first registration that matches.
    /// </summary>
    UnknownKeyed,

    /// <summary>
    /// Resolved with every key (the value type of dictionaries and key value pairs): closes every keyed open generic
    /// registration that matches.
    /// </summary>
    AllKeyed,
}

/// <summary>
/// Represents a closed generic dependency found in a constructor parameter.
/// </summary>
/// <param name="ClosedTypeName">The full name of the closed generic type.</param>
/// <param name="ClosedType">The closed generic type data.</param>
/// <param name="OpenGenericKey">The NameWithoutGeneric for looking up the open generic registration.</param>
/// <param name="Key">
/// The service key expression the dependency is resolved with, or <see langword="null"/> for a non-keyed dependency or
/// a dependency with any key (see <paramref name="KeyKind"/>).
/// </param>
/// <param name="KeyKind">How the dependency is resolved with a key.</param>
internal readonly record struct ClosedGenericDependency(
    string ClosedTypeName,
    TypeData ClosedType,
    string OpenGenericKey,
    string? Key = null,
    DependencyKeyKind KeyKind = DependencyKeyKind.Value)
{
    /// <summary>Whether the dependency is resolved with a key that is not known at compile time.</summary>
    public bool AnyKey => KeyKind >= DependencyKeyKind.Unknown;

    /// <summary>The identity of the dependency: its type as written, its key and how it is resolved with it.</summary>
    public (string TypeName, string? Key, DependencyKeyKind KeyKind) Identity => (ClosedTypeName, Key, KeyKind);

    /// <summary>Gets how a dependency resolved with <paramref name="key"/> is resolved with it.</summary>
    public static DependencyKeyKind GetKeyKind(string? key, bool isCsharpKey)
        => key is not null && isCsharpKey ? DependencyKeyKind.Csharp : DependencyKeyKind.Value;
}

/// <summary>
/// Dependency information extracted from one IServiceProvider invocation (e.g. <c>GetService&lt;T&gt;()</c>).
/// </summary>
/// <param name="ClosedGenericDependencies">Closed generic types the requested type depends on.</param>
/// <param name="CollectionElementTypeName">The element type requested as a non-keyed collection, or <see langword="null"/>.</param>
internal readonly record struct ServiceProviderInvocation(
    ImmutableEquatableArray<ClosedGenericDependency> ClosedGenericDependencies,
    string? CollectionElementTypeName)
{
    public static ServiceProviderInvocation Empty { get; } = new([], null);
}
