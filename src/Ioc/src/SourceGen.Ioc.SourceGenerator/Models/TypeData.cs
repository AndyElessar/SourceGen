namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents type information including its name and hierarchical injection metadata.
/// Generic and wrapper-specific data are provided by derived union types.
/// </summary>
/// <param name="Name">The fully qualified name of the type.</param>
/// <param name="ConstructorParameters">Constructor parameters for decorator types. Only populated for decorators.</param>
/// <param name="HasInjectConstructor">Whether the type's constructor was selected by [Inject] attribute (requires factory method for proper instantiation).</param>
/// <param name="InjectionMembers">The members (properties, fields, methods) that should be populated by dependency injection. Only populated for decorators when extractInjectionMembers is true.</param>
/// <param name="AllInterfaces">All interfaces implemented by the type. Only populated when extractHierarchy is true.</param>
/// <param name="AllBaseClasses">All base classes of the type (excluding System.Object). Only populated when extractHierarchy is true.</param>
internal record class TypeData(
    string Name,
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
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : TypeData(
        Name,
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
/// Represents wrapper type information. Base type for all wrapper kinds (collections, Lazy, Func, etc.).
/// </summary>
/// <param name="WrapperKind">The kind of wrapper this type represents.</param>
internal record class WrapperTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    WrapperKind WrapperKind,
    bool IsNestedOpenGeneric = false,
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
        TypeParameters,
        ConstructorParameters,
        HasInjectConstructor,
        InjectionMembers,
        AllInterfaces,
        AllBaseClasses);

/// <summary>
/// Base record for collection wrapper types (IEnumerable&lt;T&gt;, IReadOnlyCollection&lt;T&gt;, ICollection&lt;T&gt;,
/// IReadOnlyList&lt;T&gt;, IList&lt;T&gt;, T[]).
/// All collection wrappers share an <c>ElementType</c> property derived from <c>TypeParameters[0]</c>.
/// </summary>
internal record class CollectionWrapperTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    WrapperKind WrapperKind,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : WrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

/// <summary>
/// Represents IEnumerable&lt;T&gt; wrapper type. Supported directly by MS.DI.
/// </summary>
internal sealed record class EnumerableTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : CollectionWrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Enumerable,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

/// <summary>
/// Represents IReadOnlyCollection&lt;T&gt; wrapper type.
/// Should be resolved using GetServices&lt;T&gt;().ToArray().
/// </summary>
internal sealed record class ReadOnlyCollectionTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : CollectionWrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.ReadOnlyCollection,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

/// <summary>
/// Represents ICollection&lt;T&gt; wrapper type.
/// Should be resolved using GetServices&lt;T&gt;().ToArray().
/// </summary>
internal sealed record class CollectionTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : CollectionWrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Collection,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

/// <summary>
/// Represents IReadOnlyList&lt;T&gt; wrapper type.
/// Should be resolved using GetServices&lt;T&gt;().ToArray().
/// </summary>
internal sealed record class ReadOnlyListTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : CollectionWrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.ReadOnlyList,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

/// <summary>
/// Represents IList&lt;T&gt; wrapper type.
/// Should be resolved using GetServices&lt;T&gt;().ToArray().
/// </summary>
internal sealed record class ListTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : CollectionWrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.List,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

/// <summary>
/// Represents T[] array wrapper type.
/// Should be resolved using GetServices&lt;T&gt;().ToArray().
/// </summary>
internal sealed record class ArrayTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : CollectionWrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Array,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

/// <summary>
/// Represents Lazy&lt;T&gt; wrapper type. Lazy-initialized service wrapper.
/// </summary>
internal sealed record class LazyTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : WrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Lazy,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

/// <summary>
/// Represents Func&lt;T&gt; wrapper type. Factory delegate wrapper.
/// </summary>
internal sealed record class FuncTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : WrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Func,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

/// <summary>
/// Represents IDictionary&lt;TKey, TValue&gt; wrapper type. Dictionary of keyed services.
/// </summary>
internal sealed record class DictionaryTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : WrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Dictionary,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

/// <summary>
/// Represents KeyValuePair&lt;TKey, TValue&gt; wrapper type. Single keyed service entry.
/// </summary>
internal sealed record class KeyValuePairTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : WrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.KeyValuePair,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

/// <summary>
/// Represents Task&lt;T&gt; wrapper type. Async-initialized service wrapper.
/// Resolved via an async resolver method that awaits async inject methods.
/// </summary>
internal sealed record class TaskTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : WrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Task,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

/// <summary>
/// Wrapper classification result containing wrapper kind and extracted element type.
/// </summary>
internal readonly record struct WrapperInfo(WrapperKind Kind, INamedTypeSymbol? ElementType);

/// <summary>
/// Represents the kind of wrapper for DI injection purposes.
/// Most values have a corresponding sealed TypeData derived type; analyzer-only kinds (e.g., ValueTask) do not.
/// </summary>
internal enum WrapperKind
{
    /// <summary>
    /// Not a wrapper type.
    /// </summary>
    None = 0,

    /// <summary>
    /// IEnumerable&lt;T&gt; - supported directly by MS.DI.
    /// </summary>
    Enumerable,

    /// <summary>
    /// Read-only collection type: IReadOnlyCollection&lt;T&gt;.
    /// Should be resolved using GetServices&lt;T&gt;().ToArray().
    /// </summary>
    ReadOnlyCollection,

    /// <summary>
    /// Mutable collection interface type: ICollection&lt;T&gt;.
    /// Should be resolved using GetServices&lt;T&gt;().ToArray().
    /// </summary>
    Collection,

    /// <summary>
    /// Read-only list type: IReadOnlyList&lt;T&gt;.
    /// Should be resolved using GetServices&lt;T&gt;().ToArray().
    /// </summary>
    ReadOnlyList,

    /// <summary>
    /// Mutable list interface type: IList&lt;T&gt;.
    /// Should be resolved using GetServices&lt;T&gt;().ToArray().
    /// </summary>
    List,

    /// <summary>
    /// Array type: T[].
    /// Should be resolved using GetServices&lt;T&gt;().ToArray().
    /// </summary>
    Array,

    /// <summary>
    /// Lazy&lt;T&gt; - lazy-initialized service wrapper.
    /// </summary>
    Lazy,

    /// <summary>
    /// Func&lt;T&gt; - factory delegate wrapper.
    /// </summary>
    Func,

    /// <summary>
    /// IDictionary&lt;TKey, TValue&gt; - dictionary of keyed services.
    /// </summary>
    Dictionary,

    /// <summary>
    /// KeyValuePair&lt;TKey, TValue&gt; - single keyed service entry.
    /// </summary>
    KeyValuePair,

    /// <summary>
    /// Task&lt;T&gt; - async-initialized service wrapper.
    /// Resolved via an async resolver method that awaits async inject methods.
    /// </summary>
    Task,

    /// <summary>
    /// ValueTask&lt;T&gt; - async-initialized service wrapper.
    /// Analyzer recognizes this wrapper kind for validation paths.
    /// </summary>
    ValueTask
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
            ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
            bool HasInjectConstructor = false,
            ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
            ImmutableEquatableArray<TypeData>? AllInterfaces = null,
            ImmutableEquatableArray<TypeData>? AllBaseClasses = null) =>
            new(
                Name,
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
        /// Creates a wrapper type data for the specified <see cref="WrapperKind"/>.
        /// Returns the appropriate sealed derived type.
        /// </summary>
        public static WrapperTypeData CreateWrapper(
            string Name,
            string NameWithoutGeneric,
            bool IsOpenGeneric,
            int GenericArity,
            WrapperKind WrapperKind,
            bool IsNestedOpenGeneric = false,
            ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
            ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
            bool HasInjectConstructor = false,
            ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
            ImmutableEquatableArray<TypeData>? AllInterfaces = null,
            ImmutableEquatableArray<TypeData>? AllBaseClasses = null) => WrapperKind switch
            {
                WrapperKind.Enumerable => new EnumerableTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.ReadOnlyCollection => new ReadOnlyCollectionTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.Collection => new CollectionTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.ReadOnlyList => new ReadOnlyListTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.List => new ListTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.Array => new ArrayTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.Lazy => new LazyTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.Func => new FuncTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.Dictionary => new DictionaryTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.KeyValuePair => new KeyValuePairTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.Task => new TaskTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                _ => new WrapperTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    WrapperKind,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses)
            };

        /// <summary>
        /// Checks if the type is an array type (e.g., T[]).
        /// </summary>
        public bool IsArrayType =>
            typeData.Name.EndsWith("[]", StringComparison.Ordinal);

        /// <summary>
        /// Tries to extract the element type from an enumerable-compatible type that is NOT a <see cref="WrapperTypeData"/>.
        /// Checks direct IEnumerable&lt;T&gt;-compatible generic types, then AllInterfaces for IEnumerable&lt;T&gt; implementation.
        /// For <see cref="WrapperTypeData"/>, use <see cref="TypeDataExtensions.ElementType"/> directly instead.
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
        /// Checks if the type is a non-enumerable wrapper type that requires factory construction.
        /// Direct <c>Lazy&lt;T&gt;</c> and <c>Func&lt;T&gt;</c> (where T is not a wrapper) are excluded
        /// because they now have standalone service registrations and can be resolved via DI directly.
        /// Nested wrappers like <c>Lazy&lt;Func&lt;T&gt;&gt;</c> still require inline construction.
        /// </summary>
        public bool NeedsWrapperResolution =>
            typeData switch
            {
                LazyTypeData lazy when lazy.InstanceType is not WrapperTypeData => false,
                FuncTypeData func when func.ReturnType is not WrapperTypeData => false,
                WrapperTypeData and not EnumerableTypeData => true,
                _ => false
            };
    }

    extension(CollectionWrapperTypeData typeData)
    {
        /// <summary>
        /// Gets the element type of the collection wrapper (IEnumerable&lt;T&gt;, IReadOnlyCollection&lt;T&gt;,
        /// ICollection&lt;T&gt;, IReadOnlyList&lt;T&gt;, IList&lt;T&gt;, T[]).
        /// </summary>
        public TypeData ElementType => typeData.TypeParameters![0].Type;
    }

    extension(LazyTypeData typeData)
    {
        /// <summary>
        /// Gets the instance type of the Lazy&lt;T&gt; wrapper.
        /// </summary>
        public TypeData InstanceType => typeData.TypeParameters![0].Type;
    }

    extension(FuncTypeData typeData)
    {
        /// <summary>
        /// Gets the return type of the Func&lt;T&gt; wrapper.
        /// </summary>
        public TypeData ReturnType => typeData.TypeParameters![^1].Type;

        /// <summary>
        /// Gets the input parameter types of the Func wrapper (all type parameters except the last one).
        /// Empty for Func&lt;T&gt; (single type parameter).
        /// </summary>
        public ImmutableEquatableArray<TypeParameter> InputTypes =>
            typeData.TypeParameters is { Length: > 1 } tp
                ? tp.Take(tp.Length - 1).ToImmutableEquatableArray()
                : [];

        /// <summary>
        /// Whether this Func has input parameters (more than just the return type).
        /// </summary>
        public bool HasInputParameters => typeData.TypeParameters is { Length: > 1 };
    }

    extension(DictionaryTypeData typeData)
    {
        /// <summary>
        /// Gets the key type of the IDictionary&lt;TKey, TValue&gt; wrapper.
        /// </summary>
        public TypeData KeyType => typeData.TypeParameters![0].Type;

        /// <summary>
        /// Gets the value type of the IDictionary&lt;TKey, TValue&gt; wrapper.
        /// </summary>
        public TypeData ValueType => typeData.TypeParameters![1].Type;
    }

    extension(KeyValuePairTypeData typeData)
    {
        /// <summary>
        /// Gets the key type of the KeyValuePair&lt;TKey, TValue&gt; wrapper.
        /// </summary>
        public TypeData KeyType => typeData.TypeParameters![0].Type;

        /// <summary>
        /// Gets the value type of the KeyValuePair&lt;TKey, TValue&gt; wrapper.
        /// </summary>
        public TypeData ValueType => typeData.TypeParameters![1].Type;
    }

    extension(TaskTypeData typeData)
    {
        /// <summary>
        /// Gets the inner service type of the Task&lt;T&gt; wrapper.
        /// </summary>
        public TypeData InnerType => typeData.TypeParameters![0].Type;
    }
}
