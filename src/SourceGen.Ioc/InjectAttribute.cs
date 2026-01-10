using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Specifies that a property, field, method, or constructor should be populated by dependency injection.
/// </summary>
/// <remarks>
/// Apply this attribute to indicate that the decorated member will receive its value from the dependency injection container.<br/>
/// Generator will generate factory method in registration code to populate the member during object creation.<br/>
/// When decorated a method, the method will be called after the object is created to set up dependencies. Method should be non-static and have void return type.<br/>
/// When decorated a constructor, that constructor will be used for dependency injection instead of the primary constructor or the constructor with most parameters.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Parameter | AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class InjectAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InjectAttribute"/> class.
    /// </summary>
    public InjectAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InjectAttribute"/> class with the specified key used for dependency resolution.
    /// </summary>
    /// <param name="key">The key that identifies the dependency to be injected. Default KeyType is <see cref="KeyType.Value"/>.</param>
    public InjectAttribute(object key)
    {
        this.Key = key;
    }

    /// <summary>
    /// Gets a value specifying how to interpret <see cref="Key"/>.
    /// </summary>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <summary>
    /// Gets a key for keyed injection like [FromKeyedService]. The interpretation of the key depends on <see cref="KeyType"/>.<br/>
    /// When KeyType is <see cref="KeyType.Value"/>, this should be a primitive value, like <see cref="int"/>, <see cref="string"/> or <see langword="enum"/>.<br/>
    /// When KeyType is <see cref="KeyType.Csharp"/>, this should be a C# code snippet. You can use <see langword="nameof"/> for compile time safety.
    /// </summary>
    public object? Key { get; init; }
}
