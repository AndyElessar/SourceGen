using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Registers <see cref="ImplementationType"/> as an implementation type. Use it when the attribute is not wanted on the
/// type itself, or when the type belongs to another party.
/// </summary>
/// <param name="implementationType">The implementation type.</param>
/// <param name="lifetime">The lifetime of the registration.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterForAttribute(Type implementationType, ServiceLifetime lifetime) : Attribute
{
    /// <summary>Gets the implementation type.</summary>
    public Type ImplementationType { get; } = implementationType;

    /// <summary>Gets the lifetime of the registration.</summary>
    public ServiceLifetime Lifetime { get; } = lifetime;

    /// <summary>Gets or initializes the service types in addition to the implementation type itself.</summary>
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
