using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Makes the generator generate the <c>Add{Name}</c> extension method of <c>IServiceCollection</c> for this assembly.
/// Call only the method of the final assembly: it registers all modules that the assembly imports.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocServiceCollectionExtensionsAttribute : Attribute
{
    /// <summary>Gets or initializes the <c>{Name}</c> part of the method and class names. When it is not set, the assembly name.</summary>
    public string? Name { get; init; }
}
