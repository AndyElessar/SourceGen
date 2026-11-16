namespace SourceGen.Ioc;

/// <summary>
/// Represents a member (property, field, or method) that should be populated by dependency injection.
/// </summary>
/// <param name="Name">The name of the member, escaped with <c>@</c> when it is a keyword (such as <c>@event</c>).</param>
/// <param name="Key">The key for keyed injection; for methods, it applies to parameters without their own key.</param>
/// <param name="IsCsharpKey">
/// Whether <see cref="Key"/> is given as C# code (<c>KeyType = KeyType.Csharp</c>) rather than a constant value.
/// </param>
internal closed record class InjectionMemberData(string Name, string? Key, bool IsCsharpKey)
{
    /// <summary>
    /// The kind of member, which also orders injection stages (Property → Field → Method → AsyncMethod).
    /// </summary>
    public abstract InjectionMemberType MemberType { get; }
}

/// <summary>
/// A property or field assigned via object initializer.
/// </summary>
/// <param name="Name">The name of the member, escaped with <c>@</c> when it is a keyword (such as <c>@event</c>).</param>
/// <param name="Key">The key for keyed injection.</param>
/// <param name="Type">The type data of the member.</param>
/// <param name="IsField">Whether the member is a field rather than a property.</param>
/// <param name="IsNullable">Whether this member type is nullable (e.g., IDependency?).</param>
/// <param name="HasDefaultValue">Whether this property/field has an initializer with default value.</param>
/// <param name="DefaultValue">The C# code representation of the default value. Null means the default value is null or no initializer.</param>
/// <param name="IsCsharpKey">Whether <paramref name="Key"/> is given as C# code rather than a constant value.</param>
internal sealed record class PropertyOrFieldInjectionData(
    string Name,
    string? Key,
    TypeData Type,
    bool IsField,
    bool IsNullable = false,
    bool HasDefaultValue = false,
    string? DefaultValue = null,
    bool IsCsharpKey = false) : InjectionMemberData(Name, Key, IsCsharpKey)
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
/// <param name="Name">The name of the method, escaped with <c>@</c> when it is a keyword.</param>
/// <param name="Key">The key applied to parameters without their own key.</param>
/// <param name="Parameters">The method parameters.</param>
/// <param name="IsAsync">Whether the method returns <c>Task</c> and is awaited after object creation.</param>
/// <param name="CallThroughType">
/// The declaring type of a base class method that the call is made through (<c>((Base)s0).Init(...)</c>), because a more
/// derived type declares a method with the same name that the call on the instance could bind to instead;
/// <see langword="null"/> to call the method on the instance.
/// </param>
/// <param name="IsCsharpKey">Whether <paramref name="Key"/> is given as C# code rather than a constant value.</param>
internal sealed record class MethodInjectionData(
    string Name,
    string? Key,
    ImmutableEquatableArray<ParameterData> Parameters,
    bool IsAsync,
    TypeData? CallThroughType = null,
    bool IsCsharpKey = false) : InjectionMemberData(Name, Key, IsCsharpKey)
{
    public override InjectionMemberType MemberType => IsAsync ? InjectionMemberType.AsyncMethod : InjectionMemberType.Method;

    /// <summary>
    /// Gets the method access on <paramref name="instance"/> to invoke: <c>instance.Init</c>, or
    /// <c>((global::Base)instance).Init</c> when the call is made through <see cref="CallThroughType"/>.
    /// </summary>
    public string GetInvocationTarget(string instance)
        => CallThroughType is null ? $"{instance}.{Name}" : $"(({CallThroughType.Name}){instance}).{Name}";
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
