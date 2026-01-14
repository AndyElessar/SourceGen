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
/// <param name="CollectionKind">The kind of collection this type represents for DI injection purposes.</param>
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
    CollectionKind CollectionKind = CollectionKind.None,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
{
    /// <summary>
    /// Whether this type is a non-IEnumerable collection type (IList&lt;T&gt;, T[], IReadOnlyList&lt;T&gt;, etc.)
    /// that requires factory method for DI injection.
    /// </summary>
    public bool IsNonEnumerableCollection =>
        CollectionKind is not CollectionKind.None and not CollectionKind.Enumerable;
}

/// <summary>
/// Represents the kind of collection for DI injection purposes.
/// </summary>
internal enum CollectionKind
{
    /// <summary>
    /// Not a collection type.
    /// </summary>
    None = 0,

    /// <summary>
    /// IEnumerable&lt;T&gt; - supported directly by MS.DI.
    /// </summary>
    Enumerable,

    /// <summary>
    /// Read-only collection types: IReadOnlyCollection&lt;T&gt;, IReadOnlyList&lt;T&gt;, T[].
    /// Should be resolved using GetServices&lt;T&gt;().ToArray().
    /// </summary>
    ReadOnlyCollection,

    /// <summary>
    /// Mutable collection types: ICollection&lt;T&gt;, IList&lt;T&gt;, List&lt;T&gt;.
    /// Should be resolved using GetServices&lt;T&gt;().ToList().
    /// </summary>
    MutableCollection
}
