using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Makes a member of a type that the user does not own an injected member. It MUST be in the same assembly and the same
/// registration scope as the registration attribute of <see cref="ImplementationType"/>.
/// </summary>
/// <param name="implementationType">The implementation type to inject into.</param>
/// <param name="member">The name of the field, property, or method. <c>nameof()</c> is recommended.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocInjectMemberAttribute(Type implementationType, string member) : Attribute
{
    /// <summary>Gets the implementation type to inject into.</summary>
    public Type ImplementationType { get; } = implementationType;

    /// <summary>Gets the name of the field, property, or method.</summary>
    public string Member { get; } = member;

    /// <summary>Gets or initializes how the generator reads <see cref="Key"/>.</summary>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <summary>Gets or initializes the key to resolve. Only for a field or a property.</summary>
    public object? Key { get; init; }
}
