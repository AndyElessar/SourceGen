using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Registers a static field or property that <c>nameof()</c> names as a service instance. The registration is always a
/// singleton, and no output disposes the instance.
/// </summary>
/// <param name="instance">The field or property, as a <c>nameof()</c> expression.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocInstanceForAttribute(string instance) : Attribute
{
    /// <summary>Gets the field or property, as a <c>nameof()</c> expression.</summary>
    public string Instance { get; } = instance;

    /// <summary>Gets or initializes the service types. When it is empty, the type of the field or property is the service type.</summary>
    public Type[] ServiceTypes { get; init; } = [];

    /// <summary>Gets or initializes how the generator reads <see cref="Key"/>.</summary>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <summary>Gets or initializes the key of the service. With <see cref="KeyType.Csharp"/>, a <c>nameof()</c> expression.</summary>
    public object? Key { get; init; }

    /// <summary>Gets or initializes the tags. A registration with tags is included only when at least one tag is selected.</summary>
    public string[] Tags { get; init; } = [];
}
