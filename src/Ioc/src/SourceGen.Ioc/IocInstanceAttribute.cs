using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Registers the static field or property that has this attribute as a service instance. The registration is always a
/// singleton, and no output disposes the instance.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocInstanceAttribute : Attribute
{
    /// <summary>Gets or initializes the service types. When it is empty, the type of the field or property is the service type.</summary>
    public Type[] ServiceTypes { get; init; } = [];

    /// <summary>Gets or initializes how the generator reads <see cref="Key"/>.</summary>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <summary>Gets or initializes the key of the service. With <see cref="KeyType.Csharp"/>, a <c>nameof()</c> expression.</summary>
    public object? Key { get; init; }

    /// <summary>Gets or initializes the tags. A registration with tags is included only when at least one tag is selected.</summary>
    public string[] Tags { get; init; } = [];
}
