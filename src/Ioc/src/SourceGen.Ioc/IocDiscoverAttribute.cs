using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Declares a type that must be resolvable: a closed generic type or a wrapper type that the generator does not find
/// from the dependency sites and resolve calls. Without a key, the demand has no key.
/// </summary>
/// <param name="type">The type.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocDiscoverAttribute(Type type) : Attribute
{
    /// <summary>Gets the type.</summary>
    public Type Type { get; } = type;

    /// <summary>Gets or initializes how the generator reads <see cref="Key"/>.</summary>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <summary>Gets or initializes the key of the demand.</summary>
    public object? Key { get; init; }
}
