using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Indicates that the specified module's default settings should be import to current assembly.
/// </summary>
/// <param name="moduleType">Sets the module to import, it will look for type's <see cref="IoCRegisterDefaultsAttribute"/>.</param>
/// <remarks>
/// Usually used to import default registration settings from another module(assembly).<br/>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[Conditional("NEVER")]
public sealed class ImportModuleAttribute(Type moduleType) : Attribute
{
    /// <summary>
    /// Gets the module to import, it will look for type's <see cref="IoCRegisterDefaultsAttribute"/>.
    /// </summary>
    public Type ModuleType { get; } = moduleType;
}
