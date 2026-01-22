using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Generate Ioc container for the attributed class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocContainerAttribute : Attribute
{
    /// <summary>
    /// Gets a value indicating whether the container should be able to resolve services from external IServiceProvider
    /// and implement IServiceProviderFactory&lt;IServiceCollection&gt;.
    /// </summary>
    public bool ResolveIServiceCollection { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether only explicitly annotated on the class should be included in the generated container.
    /// </summary>
    public bool ExplicitOnly { get; init; }

    /// <summary>
    /// Gets a set of tags to include services annotated with.
    /// </summary>
    public string[] IncludeTags { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether a switch statement should be used in code generation.
    /// </summary>
    /// <remarks>
    /// Only less than 50 services will get performance benefit from using switch statement.
    /// </remarks>
    public bool UseSwitchStatement { get; init; }
}
