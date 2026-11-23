using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Registers the static method that has this attribute as a factory. The container injects the parameters of the method.
/// A return type of <c>Task&lt;T&gt;</c> makes it an asynchronous factory of <c>T</c>.
/// </summary>
/// <param name="lifetime">The lifetime of the registration.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocFactoryAttribute(ServiceLifetime lifetime) : Attribute
{
    /// <summary>Gets the lifetime of the registration.</summary>
    public ServiceLifetime Lifetime { get; } = lifetime;

    /// <summary>Gets or initializes the service types. When it is empty, the return type is the service type. A generic method MUST NOT set it.</summary>
    public Type[] ServiceTypes { get; init; } = [];

    /// <summary>Gets or initializes how the generator reads <see cref="Key"/>.</summary>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <summary>Gets or initializes the key of the service. With <see cref="KeyType.Csharp"/>, a <c>nameof()</c> expression.</summary>
    public object? Key { get; init; }

    /// <summary>Gets or initializes the tags. A registration with tags is included only when at least one tag is selected.</summary>
    public string[] Tags { get; init; } = [];
}
