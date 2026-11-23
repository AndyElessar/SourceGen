namespace SourceGen.Ioc.Model;

/// <summary>The lifetime of an instance definition. The values are the same as <c>ServiceLifetime</c>.</summary>
internal enum Lifetime
{
    /// <summary>One instance for the root.</summary>
    Singleton = 0,

    /// <summary>One instance for each scope.</summary>
    Scoped = 1,

    /// <summary>A new instance for each resolution.</summary>
    Transient = 2,
}

/// <summary>
/// A registration declaration of an implementation type (Annotations.spec, "Registration declarations"), with the
/// dependencies of its selected constructor.
/// </summary>
/// <param name="Implementation">The implementation type: its identity.</param>
/// <param name="Lifetime">The lifetime.</param>
/// <param name="ServiceTypes">The service types that the attribute gives. The implementation type itself is not in the list.</param>
/// <param name="ConstructorParameters">The parameters of the selected constructor, in order.</param>
/// <param name="IsError">
/// Whether the declaration is invalid. It has no service types and no parameters, and it makes the complete instance
/// definition an error node.
/// </param>
internal sealed record RegistrationDeclaration(
    NamedTypeRef Implementation,
    Lifetime Lifetime,
    ImmutableEquatableArray<TypeRef> ServiceTypes,
    ImmutableEquatableArray<ConstructorParameter> ConstructorParameters,
    bool IsError = false);

/// <summary>A closed definition after merging: the input of the code that generates <c>Add{Name}</c>.</summary>
/// <param name="Implementation">The implementation type.</param>
/// <param name="Lifetime">The lifetime.</param>
/// <param name="ServiceTypes">The service types: the implementation type first, then the others by sort name.</param>
/// <param name="ConstructorParameters">The constructor parameters, in order.</param>
internal sealed record ClosedDefinition(
    NamedTypeRef Implementation,
    Lifetime Lifetime,
    ImmutableEquatableArray<TypeRef> ServiceTypes,
    ImmutableEquatableArray<ConstructorParameter> ConstructorParameters);

/// <summary>A parameter of the selected constructor.</summary>
/// <param name="Type">The type of the parameter.</param>
/// <param name="ByReference">
/// Whether it is an <c>in</c> or <c>ref readonly</c> parameter. The generated code passes it a local variable with
/// <c>in</c>: a value passed to a <c>ref readonly</c> parameter gives warning CS9193.
/// </param>
internal sealed record ConstructorParameter(TypeRef Type, bool ByReference);

/// <summary>The settings of the <c>Add{Name}</c> method, read from <c>IocServiceCollectionExtensionsAttribute</c>.</summary>
/// <param name="Name">The <c>{Name}</c> part, already reduced to identifier characters.</param>
/// <param name="Namespace">The namespace of the generated class.</param>
/// <param name="AssemblyName">The name of this assembly.</param>
internal sealed record AddMethodSettings(string Name, string Namespace, string AssemblyName);

/// <summary>Everything that the code of the <c>Add{Name}</c> method needs.</summary>
/// <param name="Settings">The settings.</param>
/// <param name="Definitions">The closed definitions in their ordering.</param>
internal sealed record AddMethodModel(AddMethodSettings Settings, ImmutableEquatableArray<ClosedDefinition> Definitions);
