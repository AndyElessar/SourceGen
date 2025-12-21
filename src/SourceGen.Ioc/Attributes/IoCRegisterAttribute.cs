using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Specifies that a class should be registered with a dependency injection container, using the provided service
/// lifetime and optional service types or key.
/// </summary>
/// <remarks>Apply this attribute to a class to indicate that it should be registered for dependency injection.
/// You can specify one or more service types to register the class as, and optionally provide a key for keyed
/// registrations. This attribute is intended for use with code generation or tooling that processes IoC registrations;
/// it is not evaluated at runtime.</remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("NEVER")]
public sealed class IoCRegisterAttribute : Attribute
{
    /// <summary>
    /// The lifetime with which the service should be registered in the dependency injection container.
    /// Determines the scope of the service instance.
    /// </summary>
    public ServiceLifetime Lifetime { get; init; }

    /// <summary>
    /// Specifies whether to register all interfaces implemented.
    /// </summary>
    public bool RegisterAllInterfaces { get; init; }

    /// <summary>
    /// Specifies whether to register all base classes inherited.
    /// </summary>
    public bool RegisterAllBaseClasses { get; init; }

    /// <summary>
    /// Service types to register the class as. If none are specified, the class will be registered as itself.
    /// </summary>
    public Type[] ServiceTypes { get; init; } = [];

    /// <summary>
    /// Specifies how to interpret <see cref="Key"/>.
    /// </summary>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <summary>
    /// Specifies a key for keyed registrations. The interpretation of the key depends on the <see cref="KeyType"/>.
    /// </summary>
    public object? Key { get; init; }
}
