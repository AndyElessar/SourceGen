using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Marks the constructor to use, an injection method, or a member to inject. On a parameter, field, property, or partial
/// accessor, it can give the key to resolve, as <c>[FromKeyedServices]</c> does.
/// <list type="bullet">
/// <item><c>[IocInject]</c>: no key.</item>
/// <item><c>[IocInject(Key = "a")]</c>: the key <c>"a"</c>.</item>
/// <item><c>[IocInject(LookupMode = ServiceKeyLookupMode.InheritKey)]</c>: the key of the service that owns the member.</item>
/// <item><c>[IocInject(LookupMode = ServiceKeyLookupMode.NullKey)]</c>: explicitly no key.</item>
/// </list>
/// On a constructor or a method, <see cref="Key"/> and <see cref="LookupMode"/> MUST NOT be set.
/// </summary>
[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocInjectAttribute : Attribute
{
    /// <summary>Gets or initializes how the generator reads <see cref="Key"/>.</summary>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <summary>Gets or initializes the key to resolve. With <see cref="KeyType.Csharp"/>, a <c>nameof()</c> expression.</summary>
    public object? Key { get; init; }

    /// <summary>Gets or initializes how the key is found. With <see cref="ServiceKeyLookupMode.InheritKey"/> or <see cref="ServiceKeyLookupMode.NullKey"/>, <see cref="Key"/> MUST NOT be set.</summary>
    public ServiceKeyLookupMode LookupMode { get; init; } = ServiceKeyLookupMode.ExplicitKey;
}
