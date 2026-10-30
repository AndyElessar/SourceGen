namespace SourceGen.Ioc;

/// <summary>
/// Represents type information including its name and hierarchical injection metadata.
/// Generic and wrapper-specific data are represented by a closed record hierarchy.
/// </summary>
/// <param name="Name">The fully qualified name of the type.</param>
/// <param name="Traits">Traits used to check special type parameter constraints. Only recorded for type arguments; <see langword="default"/> otherwise.</param>
/// <param name="ConstructorParameters">Constructor parameters for implementations and decorators, not recursively for their dependencies.</param>
/// <param name="HasInjectConstructor">Whether the type's constructor was selected by [Inject] attribute (requires factory method for proper instantiation).</param>
/// <param name="InjectionMembers">The members (properties, fields, methods) that should be populated by dependency injection. Only populated for decorators when extractInjectionMembers is true.</param>
/// <param name="AllInterfaces">All interfaces implemented by the type. Only populated when extractHierarchy is true.</param>
/// <param name="AllBaseClasses">All base classes of the type (excluding System.Object). Only populated when extractHierarchy is true.</param>
internal closed record class TypeData(
    string Name,
    TypeTraits Traits,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null);

/// <summary>
/// Represents a non-generic type without wrapper-specific data.
/// </summary>
internal sealed record class SimpleTypeData(
    string Name,
    TypeTraits Traits,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : TypeData(
        Name,
        Traits,
        ConstructorParameters,
        HasInjectConstructor,
        InjectionMembers,
        AllInterfaces,
        AllBaseClasses);

/// <summary>
/// Represents generic type information.
/// </summary>
internal closed record class GenericTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : TypeData(
        Name,
        Traits,
        ConstructorParameters,
        HasInjectConstructor,
        InjectionMembers,
        AllInterfaces,
        AllBaseClasses);

/// <summary>
/// Represents a generic type that is not a type parameter or a recognized wrapper.
/// </summary>
internal sealed record class NamedGenericTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    TypeTraits Traits,
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
        Traits,
        IsNestedOpenGeneric,
        TypeParameters,
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
        GenericArity: 0,
        Traits: default);

/// <summary>
/// Represents wrapper type information. Base type for all wrapper kinds (collections, Lazy, Func, etc.).
/// </summary>
/// <param name="WrapperKind">The kind of wrapper this type represents.</param>
internal closed record class WrapperTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    WrapperKind WrapperKind,
    TypeTraits Traits,
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
        Traits,
        IsNestedOpenGeneric,
        TypeParameters,
        ConstructorParameters,
        HasInjectConstructor,
        InjectionMembers,
        AllInterfaces,
        AllBaseClasses);

/// <summary>
/// Represents a wrapper kind without a specialized type data subtype.
/// </summary>
internal sealed record class OtherWrapperTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    WrapperKind WrapperKind,
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
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
        WrapperKind,
        Traits,
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
internal closed record class CollectionWrapperTypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric,
    int GenericArity,
    WrapperKind WrapperKind,
    TypeTraits Traits,
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
        Traits,
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
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : CollectionWrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Enumerable, Traits,
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
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : CollectionWrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.ReadOnlyCollection, Traits,
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
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : CollectionWrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Collection, Traits,
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
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : CollectionWrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.ReadOnlyList, Traits,
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
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : CollectionWrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.List, Traits,
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
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : CollectionWrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Array, Traits,
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
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : WrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Lazy, Traits,
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
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : WrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Func, Traits,
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
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : WrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Dictionary, Traits,
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
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : WrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.KeyValuePair, Traits,
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
    TypeTraits Traits,
    bool IsNestedOpenGeneric = false,
    ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
    ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
    bool HasInjectConstructor = false,
    ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
    ImmutableEquatableArray<TypeData>? AllInterfaces = null,
    ImmutableEquatableArray<TypeData>? AllBaseClasses = null)
    : WrapperTypeData(
        Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
        WrapperKind.Task, Traits,
        IsNestedOpenGeneric, TypeParameters,
        ConstructorParameters, HasInjectConstructor, InjectionMembers,
        AllInterfaces, AllBaseClasses);

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
