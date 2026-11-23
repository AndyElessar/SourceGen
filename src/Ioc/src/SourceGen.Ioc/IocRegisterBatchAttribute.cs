using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Registers several implementation types with one lifetime and one set of decorators. Each implementation type is
/// registered with each service type, and it MUST implement each of them. The registrations have no key.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterBatchAttribute : Attribute
{
    /// <summary>Gets or initializes the lifetime of each registration.</summary>
    public required ServiceLifetime Lifetime { get; init; }

    /// <summary>Gets or initializes the service types to register.</summary>
    public required Type[] ServiceTypes { get; init; }

    /// <summary>Gets or initializes the implementation types to register.</summary>
    public required Type[] ImplementationTypes { get; init; }

    /// <summary>Gets or initializes the decorators. The first item is the outermost layer.</summary>
    public Type[] Decorators { get; init; } = [];

    /// <summary>Gets or initializes the tags. A registration with tags is included only when at least one tag is selected.</summary>
    public string[] Tags { get; init; } = [];
}
