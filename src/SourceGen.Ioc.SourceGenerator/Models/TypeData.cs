namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents type information including its name and whether it is an open generic type.
/// </summary>
/// <param name="Name">The fully qualified name of the type.</param>
/// <param name="NameWithoutGeneric">The fully qualified name of the type without generic parameters.</param>
/// <param name="IsOpenGeneric">Whether the type is an open generic type.</param>
/// <param name="GenericArity">The number of generic type parameters.</param>
/// <param name="IsNestedOpenGeneric">Whether the type contains nested open generic type arguments (e.g., IGeneric&lt;IGeneric2&lt;T&gt;&gt;).</param>
/// <param name="IsTypeParameter">Whether this type represents a type parameter (e.g., T, TRequest). Determined from TypeKind.TypeParameter at creation time.</param>
/// <param name="IsNonEnumerableCollection">Whether this type is a non-IEnumerable collection type (IList&lt;T&gt;, T[], IReadOnlyList&lt;T&gt;, etc.) that requires factory method for DI injection.</param>
/// <param name="TypeParameters">The generic type parameters with their names, resolved types, implemented interfaces, and constraints.</param>
/// <param name="ConstructorParameters">Constructor parameters for decorator types. Only populated for decorators.</param>
/// <param name="HasInjectConstructor">Whether the type's constructor was selected by [Inject] attribute (requires factory method for proper instantiation).</param>
/// <param name="AllInterfaces">All interfaces implemented by the type. Only populated when extractHierarchy is true.</param>
/// <param name="AllBaseClasses">All base classes of the type (excluding System.Object). Only populated when extractHierarchy is true.</param>
internal sealed record class TypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    bool IsTypeParameter = false,
    bool IsNonEnumerableCollection = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null);
