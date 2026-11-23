using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace SourceGen.Ioc;

/// <summary>Carries the module information of an assembly as JSON. The generator of an importer reads it.</summary>
/// <param name="json">The module information.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
[Experimental("SGIOCEXP001", Message = "This type is only for the code that SourceGen.Ioc generates. Do not use it directly.")]
public sealed class IocModuleInfoAttribute(string json) : Attribute
{
    /// <summary>Gets the module information.</summary>
    public string Json { get; } = json;
}
