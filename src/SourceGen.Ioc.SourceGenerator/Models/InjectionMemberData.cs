namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents a member (property, field, or method) that should be populated by dependency injection.
/// </summary>
/// <param name="MemberType">The type of member (Property, Field, or Method).</param>
/// <param name="Name">The name of the member.</param>
/// <param name="Type">The type data of the member (for property/field) or method parameters.</param>
/// <param name="Parameters">The method parameters (only for methods).</param>
/// <param name="Key">The key for keyed injection.</param>
/// <param name="KeyType">How to interpret the key (Value or Csharp code).</param>
internal sealed record class InjectionMemberData(
    InjectionMemberType MemberType,
    string Name,
    TypeData? Type,
    ImmutableEquatableArray<ParameterData>? Parameters,
    string? Key,
    int KeyType);

/// <summary>
/// The type of injection member.
/// </summary>
internal enum InjectionMemberType
{
    /// <summary>
    /// A property to be set via object initializer.
    /// </summary>
    Property,

    /// <summary>
    /// A field to be set via object initializer.
    /// </summary>
    Field,

    /// <summary>
    /// A method to be called after object creation.
    /// </summary>
    Method
}
