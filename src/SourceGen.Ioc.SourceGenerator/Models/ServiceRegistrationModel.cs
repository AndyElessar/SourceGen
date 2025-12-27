namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents a service registration for dependency injection.
/// </summary>
/// <param name="ServiceType">The service type to register.</param>
/// <param name="ServiceTypeArity">The number of generic type parameters for the service type.</param>
/// <param name="ImplementationType">The implementation type.</param>
/// <param name="ImplementationTypeArity">The number of generic type parameters for the implementation type.</param>
/// <param name="Lifetime">The service lifetime (Singleton, Scoped, Transient).</param>
/// <param name="Key">The key for keyed registration, or null for non-keyed.</param>
/// <param name="KeyType">How to interpret the key (Value or Csharp code).</param>
/// <param name="IsOpenGeneric">Whether this is an open generic registration.</param>
/// <param name="Decorators">The decorator types to apply, in order from outermost to innermost.</param>
/// <param name="AllServiceTypes">All service types (interfaces and base classes) that the implementation type can be assigned to. Used to determine which decorator constructor parameters should receive the decorated instance.</param>
internal sealed record class ServiceRegistrationModel(
    string ServiceType,
    int ServiceTypeArity,
    string ImplementationType,
    int ImplementationTypeArity,
    ServiceLifetime Lifetime,
    string? Key,
    int KeyType,
    bool IsOpenGeneric,
    ImmutableEquatableArray<TypeData> Decorators,
    ImmutableEquatableArray<string> AllServiceTypes);
