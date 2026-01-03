using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Specifies that a property, field, or method should be populated by dependency injection.
/// </summary>
/// <remarks>
/// Apply this attribute to indicate that the decorated member will receive its value from the dependency injection container.<br/>
/// Generator will generate factory method in registration code to populate the member during object creation.<br/>
/// When decorated a method, the method will be called after the object is created to set up dependencies. Method should be non-static and have void return type.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class InjectAttribute : Attribute
{
    /// <summary>
    /// Gets a value specifying how to interpret <see cref="Key"/>.
    /// </summary>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <summary>
    /// Gets a key for keyed injection like [FromKeyedService]. The interpretation of the key depends on the <see cref="KeyType"/>.<br/>
    /// When <see cref="KeyType"/> is <see cref="KeyType.Value"/>, this should be a primitive value, like <see cref="int"/>, <see cref="string"/> or <see langword="enum"/>.<br/>
    /// When <see cref="KeyType"/> is <see cref="KeyType.Csharp"/>, this should be a C# code snippet. You can use <see langword="nameof"/> for compile time safety.
    /// </summary>
    public object? Key { get; init; }
}
