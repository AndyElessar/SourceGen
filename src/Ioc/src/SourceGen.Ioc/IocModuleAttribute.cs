using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Makes the assembly a module: the generator generates the module output on the class, so that other assemblies can
/// import its registrations. An assembly can have one module class at most.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocModuleAttribute : Attribute;
