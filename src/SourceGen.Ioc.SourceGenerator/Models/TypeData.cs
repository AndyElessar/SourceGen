namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents type information including its name and whether it is an open generic type.
/// </summary>
/// <param name="Name">The fully qualified name of the type.</param>
/// <param name="NameWithoutGeneric">The fully qualified name of the type without generic parameters.</param>
/// <param name="IsOpenGeneric">Whether the type is an open generic type.</param>
/// <param name="GenericArity">The number of generic type parameters.</param>
/// <param name="IsNestedOpenGeneric">Whether the type contains nested open generic type arguments (e.g., IGeneric&lt;IGeneric2&lt;T&gt;&gt;).</param>
/// <param name="TypeParameterNames">The names of the generic type parameters (e.g., ["TRequest", "TResponse"]). Only populated for open generic types.</param>
/// <param name="ConstructorParameters">Constructor parameters for decorator types. Only populated for decorators.</param>
internal sealed record class TypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<string>? TypeParameterNames = null,
    ImmutableEquatableArray<ConstructorParameterData>? ConstructorParameters = null);
