namespace SourceGen.Ioc;

/// <summary>
/// Represents a member (property, field, or method) that should be populated by dependency injection.
/// </summary>
/// <param name="Name">The name of the member.</param>
/// <param name="Key">The key for keyed injection; for methods, it applies to parameters without their own key.</param>
internal closed record class InjectionMemberData(string Name, string? Key)
{
    /// <summary>
    /// The kind of member, which also orders injection stages (Property → Field → Method → AsyncMethod).
    /// </summary>
    public abstract InjectionMemberType MemberType { get; }
}

/// <summary>
/// A property or field assigned via object initializer.
/// </summary>
/// <param name="Name">The name of the member.</param>
/// <param name="Key">The key for keyed injection.</param>
/// <param name="Type">The type data of the member.</param>
/// <param name="IsField">Whether the member is a field rather than a property.</param>
/// <param name="IsNullable">Whether this member type is nullable (e.g., IDependency?).</param>
/// <param name="HasDefaultValue">Whether this property/field has an initializer with default value.</param>
/// <param name="DefaultValue">The C# code representation of the default value. Null means the default value is null or no initializer.</param>
internal sealed record class PropertyOrFieldInjectionData(
    string Name,
    string? Key,
    TypeData Type,
    bool IsField,
    bool IsNullable = false,
    bool HasDefaultValue = false,
    string? DefaultValue = null) : InjectionMemberData(Name, Key)
{
    public override InjectionMemberType MemberType => IsField ? InjectionMemberType.Field : InjectionMemberType.Property;

    /// <summary>
    /// Whether the default value is null. Only meaningful when HasDefaultValue is true.
    /// </summary>
    public bool DefaultValueIsNull => HasDefaultValue && DefaultValue is null;
}

/// <summary>
/// A method called after object creation.
/// </summary>
/// <param name="Name">The name of the method.</param>
/// <param name="Key">The key applied to parameters without their own key.</param>
/// <param name="Parameters">The method parameters.</param>
/// <param name="IsAsync">Whether the method returns <c>Task</c> and is awaited after object creation.</param>
internal sealed record class MethodInjectionData(
    string Name,
    string? Key,
    ImmutableEquatableArray<ParameterData> Parameters,
    bool IsAsync) : InjectionMemberData(Name, Key)
{
    public override InjectionMemberType MemberType => IsAsync ? InjectionMemberType.AsyncMethod : InjectionMemberType.Method;
}

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
    Method,

    /// <summary>
    /// An async method (returning <c>Task</c>) to be awaited after object creation.
    /// </summary>
    AsyncMethod
}
