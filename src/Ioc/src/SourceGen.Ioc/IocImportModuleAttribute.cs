using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Imports a module of another assembly. The type MUST have <see cref="IocModuleAttribute"/>.
/// </summary>
/// <param name="module">The module class.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocImportModuleAttribute(Type module) : Attribute
{
    /// <summary>Gets the module class.</summary>
    public Type Module { get; } = module;
}
