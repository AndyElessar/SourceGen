using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Specifies that a class should be registered with a dependency injection container, using the provided service
/// lifetime and optional service types or key.
/// </summary>
/// <remarks>
/// Apply this attribute to a class to indicate that it should be registered for dependency injection.<br/>
/// You can specify one or more service types to register the class as, and optionally provide a key for keyed registrations.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN_IOC")]
public sealed class IoCRegisterAttribute : Attribute
{
    /// <summary>
    /// Gets the lifetime with which the service should be registered in the dependency injection container.
    /// Determines the scope of the service instance.
    /// </summary>
    public ServiceLifetime Lifetime { get; init; }

    /// <summary>
    /// Gets a value indicating whether to register all interfaces implemented.
    /// </summary>
    public bool RegisterAllInterfaces { get; init; }

    /// <summary>
    /// Gets a value indicating whether to register all base classes inherited.
    /// </summary>
    public bool RegisterAllBaseClasses { get; init; }

    /// <summary>
    /// Gets the collection of service types to register the class as.
    /// </summary>
    public Type[] ServiceTypes { get; init; } = [];

    /// <summary>
    /// Gets a value specifying how to interpret <see cref="Key"/>.
    /// </summary>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <summary>
    /// Gets a key for keyed registrations. The interpretation of the key depends on the <see cref="KeyType"/>.<br/>
    /// When <see cref="KeyType"/> is <see cref="KeyType.Value"/>, this should be a primitive value, like <see cref="int"/>, <see cref="string"/> or <see langword="enum"/>.<br/>
    /// When <see cref="KeyType"/> is <see cref="KeyType.Csharp"/>, this should be a C# code snippet. You can use <see langword="nameof"/> for compile time safety.
    /// </summary>
    public object? Key { get; init; }

    /// <summary>
    /// Gets the collection of decorator types to apply to the target type.
    /// </summary>
    /// <remarks>
    /// Each type in the collection should represent a decorator that implements service types.<br/>
    /// The order of decorators in the array determines the order of execute (which means the first decorator in the array is the outermost).
    /// </remarks>
    public Type[] Decorators { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether to exclude this class from default registrations.
    /// </summary>
    public bool ExcludeFromDefault { get; init; }

    /// <summary>
    /// Gets the collection of tags associated with this registration.<br/>
    /// Will generate registrations for each tag specified.
    /// </summary>
    public string[] Tags { get; init; } = [];
}
