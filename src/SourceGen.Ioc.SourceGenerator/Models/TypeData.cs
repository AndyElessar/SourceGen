namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents type information including its name and whether it is an open generic type.
/// </summary>
/// <param name="Name">The fully qualified name of the type.</param>
/// <param name="NameWithoutGeneric">The fully qualified name of the type without generic parameters.</param>
/// <param name="IsOpenGeneric">Whether the type is an open generic type.</param>
internal sealed record class TypeData(
    string Name,
    string NameWithoutGeneric,
    bool IsOpenGeneric);
