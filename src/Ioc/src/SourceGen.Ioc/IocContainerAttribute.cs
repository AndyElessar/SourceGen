using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// The generator generates the IoC container on the partial class that has this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocContainerAttribute : Attribute
{
    /// <summary>Gets or initializes a value indicating whether the container has a fallback provider. Without one, a missing dependency is a compile-time error.</summary>
    public bool IntegrateServiceProvider { get; init; } = true;

    /// <summary>
    /// Gets or initializes a value indicating whether the container includes only the registrations and imports that are on its class. It MUST NOT be combined
    /// with <see cref="IncludeTags"/> or with <see cref="IocModuleAttribute"/> on the same class.
    /// </summary>
    public bool ExplicitOnly { get; init; }

    /// <summary>Gets or initializes the tags to include.</summary>
    public string[] IncludeTags { get; init; } = [];

    /// <summary>Gets or initializes the thread safety strategy for the first creation of singleton and scoped services.</summary>
    public ThreadSafeStrategy ThreadSafeStrategy { get; init; } = ThreadSafeStrategy.Lock;

    /// <summary>Gets or initializes which services are created when the root or a scope is created.</summary>
    public EagerResolveOptions EagerResolveOptions { get; init; } = EagerResolveOptions.Singleton;
}
