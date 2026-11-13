namespace SourceGen.Ioc;

/// <summary>
/// Represents a generic type parameter with its name, resolved type, and constraints.
/// Combines type argument information (for closed generics) with constraint information (for constraint checking).
/// </summary>
/// <param name="ParameterName">The name of the type parameter (e.g., "TRequest").</param>
/// <param name="Type">The resolved type data. For open generic types, Name equals ParameterName. For closed generic types, contains the actual type with its interfaces.</param>
/// <param name="ConstraintTypes">The type constraints (e.g., interfaces or base classes).</param>
/// <param name="HasValueTypeConstraint">Whether the type parameter has the 'struct' constraint.</param>
/// <param name="HasReferenceTypeConstraint">Whether the type parameter has the 'class' constraint.</param>
/// <param name="HasUnmanagedTypeConstraint">Whether the type parameter has the 'unmanaged' constraint.</param>
/// <param name="HasNotNullConstraint">Whether the type parameter has the 'notnull' constraint.</param>
/// <param name="HasConstructorConstraint">Whether the type parameter has the 'new()' constraint.</param>
/// <param name="IsCovariant">Whether the type parameter is covariant (<c>out</c>), so a reference type argument converts to its supertypes.</param>
internal sealed record class TypeParameter(
    string ParameterName,
    TypeData Type,
    ImmutableEquatableArray<TypeData>? ConstraintTypes = null,
    bool HasValueTypeConstraint = false,
    bool HasReferenceTypeConstraint = false,
    bool HasUnmanagedTypeConstraint = false,
    bool HasNotNullConstraint = false,
    bool HasConstructorConstraint = false,
    bool IsCovariant = false);
