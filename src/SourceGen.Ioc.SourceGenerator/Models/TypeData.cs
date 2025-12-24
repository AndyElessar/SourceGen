namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents type information including its name and whether it is an open generic type.
/// </summary>
/// <param name="Name">The fully qualified name of the type.</param>
/// <param name="NameWithoutGeneric">The fully qualified name of the type without generic parameters.</param>
/// <param name="IsOpenGeneric">Whether the type is an open generic type.</param>
/// <param name="GenericArity">The number of generic type parameters.</param>
/// <param name="IsNestedOpenGeneric">Whether the type contains nested open generic type arguments (e.g., IGeneric&lt;IGeneric2&lt;T&gt;&gt;).</param>
internal sealed record class TypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false);
