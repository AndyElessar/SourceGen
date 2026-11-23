using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Registers the class that has this attribute as an implementation type.
/// The class itself is always a service type of the registration.
/// </summary>
/// <param name="lifetime">The lifetime of the registration.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterAttribute(ServiceLifetime lifetime) : Attribute
{
    /// <summary>Gets the lifetime of the registration.</summary>
    public ServiceLifetime Lifetime { get; } = lifetime;

    /// <summary>Gets or initializes the service types in addition to the class itself. An unbound generic expands to each closed form that the class implements.</summary>
    public Type[] ServiceTypes { get; init; } = [];

    /// <summary>Gets or initializes how the generator reads <see cref="Key"/>.</summary>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <summary>Gets or initializes the key of the service. With <see cref="KeyType.Csharp"/>, a <c>nameof()</c> expression.</summary>
    public object? Key { get; init; }

    /// <summary>Gets or initializes the decorators. The first item is the outermost layer.</summary>
    public Type[] Decorators { get; init; } = [];

    /// <summary>Gets or initializes the tags. A registration with tags is included only when at least one tag is selected.</summary>
    public string[] Tags { get; init; } = [];
}
