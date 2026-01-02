namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents a service registration for dependency injection.
/// </summary>
/// <param name="ServiceType">The service type data to register.</param>
/// <param name="ImplementationType">The implementation type data.</param>
/// <param name="Lifetime">The service lifetime (Singleton, Scoped, Transient).</param>
/// <param name="Key">The key for keyed registration, or null for non-keyed.</param>
/// <param name="KeyType">How to interpret the key (Value or Csharp code).</param>
/// <param name="IsOpenGeneric">Whether this is an open generic registration.</param>
/// <param name="Decorators">The decorator types to apply (with pre-computed IsServiceParameter), in order from outermost to innermost.</param>
/// <param name="InjectionMembers">The members (properties, fields, methods) that should be populated by dependency injection.</param>
internal sealed record class ServiceRegistrationModel(
    TypeData ServiceType,
    TypeData ImplementationType,
    ServiceLifetime Lifetime,
    string? Key,
    int KeyType,
    bool IsOpenGeneric,
    ImmutableEquatableArray<TypeData> Decorators,
    ImmutableEquatableArray<InjectionMemberData> InjectionMembers);
