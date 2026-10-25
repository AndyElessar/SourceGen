namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Traits of a type argument that decide whether it satisfies the <c>class</c>, <c>struct</c>, <c>unmanaged</c>, and
/// <c>new()</c> constraints of a type parameter.
/// </summary>
[Flags]
internal enum TypeTraits
{
    /// <summary>The traits were not recorded.</summary>
    None = 0,

    /// <summary>The traits were recorded; without this flag the other flags carry no information.</summary>
    Known = 1,

    /// <summary>The type is a reference type.</summary>
    ReferenceType = 2,

    /// <summary>The type is a value type other than <see cref="Nullable{T}"/>.</summary>
    NonNullableValueType = 4,

    /// <summary>The type is an unmanaged type.</summary>
    Unmanaged = 8,

    /// <summary>The type is a value type or a non-abstract class with a public parameterless constructor.</summary>
    DefaultConstructor = 16,
}
