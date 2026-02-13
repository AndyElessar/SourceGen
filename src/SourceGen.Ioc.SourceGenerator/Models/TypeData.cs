namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents type information including its name and hierarchical injection metadata.
/// Generic and wrapper-specific data are provided by derived union types.
/// </summary>
/// <param name="Name">The fully qualified name of the type.</param>
/// <param name="IsBuiltInType">Whether this type itself is a built-in/primitive type that cannot be resolved from DI.</param>
/// <param name="ConstructorParameters">Constructor parameters for decorator types. Only populated for decorators.</param>
/// <param name="HasInjectConstructor">Whether the type's constructor was selected by [Inject] attribute (requires factory method for proper instantiation).</param>
/// <param name="InjectionMembers">The members (properties, fields, methods) that should be populated by dependency injection. Only populated for decorators when extractInjectionMembers is true.</param>
/// <param name="AllInterfaces">All interfaces implemented by the type. Only populated when extractHierarchy is true.</param>
/// <param name="AllBaseClasses">All base classes of the type (excluding System.Object). Only populated when extractHierarchy is true.</param>
internal record class TypeData(
    string Name,
    bool IsBuiltInType = false,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null);

/// <summary>
/// Represents generic type information.
/// </summary>
internal record class GenericTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    bool IsBuiltInType = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : TypeData(
        Name,
        IsBuiltInType,
        ConstructorParameters,
        HasInjectConstructor,
        InjectionMembers,
        AllInterfaces,
        AllBaseClasses);

/// <summary>
/// Represents a generic type parameter placeholder (e.g., T, TRequest).
/// Inherits from <see cref="GenericTypeData"/> with fixed IsOpenGeneric = true and GenericArity = 0.
/// </summary>
internal sealed record class TypeParameterTypeData(
    string Name,
    string NameWithoutGeneric)
    : GenericTypeData(
        Name,
        NameWithoutGeneric,
        IsOpenGeneric: true,
        GenericArity: 0);

/// <summary>
/// Represents wrapper type information.
/// </summary>
internal record class WrapperTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    bool IsBuiltInType = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : GenericTypeData(
        Name,
        NameWithoutGeneric,
        IsOpenGeneric,
        GenericArity,
        IsNestedOpenGeneric,
        IsBuiltInType,
        TypeParameters,
        ConstructorParameters,
        HasInjectConstructor,
        InjectionMembers,
        AllInterfaces,
        AllBaseClasses);

/// <summary>
/// Represents collection wrapper type information.
/// </summary>
internal sealed record class CollectionTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    bool IsBuiltInType = false,
    CollectionKind CollectionKind = CollectionKind.None,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : WrapperTypeData(
        Name,
        NameWithoutGeneric,
        IsOpenGeneric,
        GenericArity,
        IsNestedOpenGeneric,
        IsBuiltInType,
        TypeParameters,
        ConstructorParameters,
        HasInjectConstructor,
        InjectionMembers,
        AllInterfaces,
        AllBaseClasses);

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
    ReadOnlyCollection
}

internal static class TypeDataExtensions
{
    /// <summary>
    /// Factory methods and common type checks for TypeData and its derived types.
    /// </summary>
    /// <param name="typeData">The type data to check.</param>
    extension(TypeData typeData)
    {
        /// <summary>
        /// Creates a non-generic non-collection type data.
        /// </summary>
        public static TypeData CreateSimple(
            string Name,
            bool IsBuiltInType = false,
            ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
            bool HasInjectConstructor = false,
            ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
            ImmutableEquatableArray<TypeData>? AllInterfaces = null,
            ImmutableEquatableArray<TypeData>? AllBaseClasses = null) =>
            new(
                Name,
                IsBuiltInType,
                ConstructorParameters,
                HasInjectConstructor,
                InjectionMembers,
                AllInterfaces,
                AllBaseClasses);

        /// <summary>
        /// Creates a generic type data.
        /// </summary>
        public static GenericTypeData CreateGeneric(
            string Name,
            string NameWithoutGeneric,
            bool IsOpenGeneric,
            int GenericArity,
            bool IsNestedOpenGeneric = false,
            bool IsBuiltInType = false,
            ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
            ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
            bool HasInjectConstructor = false,
            ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
            ImmutableEquatableArray<TypeData>? AllInterfaces = null,
            ImmutableEquatableArray<TypeData>? AllBaseClasses = null) =>
            new(
                Name,
                NameWithoutGeneric,
                IsOpenGeneric,
                GenericArity,
                IsNestedOpenGeneric,
                IsBuiltInType,
                TypeParameters,
                ConstructorParameters,
                HasInjectConstructor,
                InjectionMembers,
                AllInterfaces,
                AllBaseClasses);

        /// <summary>
        /// Creates a type parameter placeholder (e.g., T, TRequest).
        /// </summary>
        public static TypeParameterTypeData CreateTypeParameter(string Name) =>
            new(Name, Name);

        /// <summary>
        /// Creates a collection wrapper type data.
        /// </summary>
        public static CollectionTypeData CreateCollection(
            string Name,
            string NameWithoutGeneric,
            bool IsOpenGeneric,
            int GenericArity,
            bool IsNestedOpenGeneric = false,
            bool IsBuiltInType = false,
            CollectionKind CollectionKind = CollectionKind.None,
            ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
            ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
            bool HasInjectConstructor = false,
            ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
            ImmutableEquatableArray<TypeData>? AllInterfaces = null,
            ImmutableEquatableArray<TypeData>? AllBaseClasses = null) =>
            new(
                Name,
                NameWithoutGeneric,
                IsOpenGeneric,
                GenericArity,
                IsNestedOpenGeneric,
                IsBuiltInType,
                CollectionKind,
                TypeParameters,
                ConstructorParameters,
                HasInjectConstructor,
                InjectionMembers,
                AllInterfaces,
                AllBaseClasses);

        /// <summary>
        /// Checks if the type is an array type (e.g., T[]).
        /// </summary>
        public bool IsArrayType =>
            typeData.Name.EndsWith("[]", StringComparison.Ordinal);

        /// <summary>
        /// Tries to extract the element type from an enumerable-compatible type that is NOT a <see cref="CollectionTypeData"/>.
        /// Checks direct IEnumerable&lt;T&gt;-compatible generic types, then AllInterfaces for IEnumerable&lt;T&gt; implementation.
        /// For <see cref="CollectionTypeData"/>, use <see cref="TypeDataExtensions.ElementType"/> directly instead.
        /// </summary>
        /// <returns>The element type if this is an enumerable-compatible type; otherwise, null.</returns>
        public TypeData? TryGetEnumerableElementType()
        {
            // For closed generic dependency extraction, allow direct IEnumerable<T>-compatible generic types.
            if(typeData is GenericTypeData
                {
                    GenericArity: 1,
                    TypeParameters: { Length: 1 } directTypeParameters
                } directGenericTypeData
                && IsEnumerableType(directGenericTypeData.NameWithoutGeneric))
            {
                return directTypeParameters[0].Type;
            }

            // Check AllInterfaces for IEnumerable<T> implementation
            if(typeData.AllInterfaces is { Length: > 0 })
            {
                foreach(var iface in typeData.AllInterfaces)
                {
                    // Look for IEnumerable<T> (with exactly one type argument)
                    if(iface is GenericTypeData { GenericArity: 1 } genericInterface
                        && IsEnumerableType(genericInterface.NameWithoutGeneric))
                    {
                        if(genericInterface.TypeParameters is { Length: 1 } ifaceTypeParams)
                        {
                            return ifaceTypeParams[0].Type;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Determines whether this type is a built-in type that can't be resolved from DI.
        /// For collections, checks whether the element type is built-in.
        /// For other types, checks the type itself.
        /// </summary>
        public bool IsBuiltInTypeResolvable => 
            typeData is CollectionTypeData c
            ? c.ElementType.IsBuiltInType
            : typeData.IsBuiltInType;

        /// <summary>
        /// Checks if the type is a non-enumerable collection type.
        /// </summary>
        public bool IsNonEnumerableCollection =>
            typeData is CollectionTypeData { CollectionKind: not CollectionKind.None and not CollectionKind.Enumerable };
    }

    extension(CollectionTypeData typeData)
    {
        public TypeData ElementType => typeData.TypeParameters![0].Type;
    }
}
