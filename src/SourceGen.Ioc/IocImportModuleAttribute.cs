using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Indicates that the specified module's default settings should be import to current assembly.
/// </summary>
/// <param name="moduleType">Sets the module to import, it will look for type's and assembly's <see cref="IocRegisterDefaultsAttribute"/>.</param>
/// <remarks>
/// Use for import default registration settings from another module(assembly).<br/>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocImportModuleAttribute(Type moduleType) : Attribute
{
    /// <summary>
    /// Gets the module to import, it will look for type's and assembly's <see cref="IocRegisterDefaultsAttribute"/>.
    /// </summary>
    public Type ModuleType { get; } = moduleType;
}

#if NET7_0_OR_GREATER

/// <summary>
/// Indicates that the specified module's default settings should be import to current assembly.
/// </summary>
/// <typeparam name="T">Sets the module to import, it will look for type's and assembly's <see cref="IocRegisterDefaultsAttribute"/>.</typeparam>
/// <remarks>
/// Use for import default registration settings from another module(assembly).<br/>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocImportModuleAttribute<T> : Attribute
{
}

#endif