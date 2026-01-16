namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents parameter information.
/// </summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Type">The type data of the parameter.</param>
/// <param name="IsNullable">Whether this parameter type is nullable (e.g., IDependency?).</param>
/// <param name="HasDefaultValue">Whether this parameter has an explicit default value.</param>
/// <param name="DefaultValue">The C# code representation of the default value. Null means the default value is null.</param>
/// <param name="ServiceKey">The key for keyed service resolution from [FromKeyedServices] or [Inject] attribute if present.</param>
/// <param name="HasInjectAttribute">Whether this parameter has [Inject] attribute (not [FromKeyedServices], which MS.DI handles automatically).</param>
/// <param name="HasServiceKeyAttribute">Whether this parameter has [ServiceKey] attribute from Microsoft.Extensions.DependencyInjection.</param>
/// <param name="HasFromKeyedServicesAttribute">Whether this parameter has [FromKeyedServices] attribute from Microsoft.Extensions.DependencyInjection.</param>
internal sealed record class ParameterData(
    string Name,
    TypeData Type,
    bool IsNullable = false,
    bool HasDefaultValue = false,
    string? DefaultValue = null,
    string? ServiceKey = null,
    bool HasInjectAttribute = false,
    bool HasServiceKeyAttribute = false,
    bool HasFromKeyedServicesAttribute = false)
{
    /// <summary>
    /// Whether the parameter is optional (nullable type or has default value).
    /// Used to determine whether to use GetService vs GetRequiredService.
    /// </summary>
    public bool IsOptional => IsNullable || HasDefaultValue;
    /// <summary>
    /// Whether the default value is null. Only meaningful when HasDefaultValue is true.
    /// </summary>
    public bool DefaultValueIsNull => HasDefaultValue && DefaultValue is null;
    /// <summary>
    /// Determines whether this parameter is unresolvable from dependency injection.
    /// A parameter is unresolvable if:
    /// - Its type is a built-in type or collection of built-in types
    /// - It does not have [IocInject], [ServiceKey], or [FromKeyedServices] attribute
    /// - It does not have a default value
    /// </summary>
    public bool IsUnresolvable =>
        Type.IsBuiltInTypeOrBuiltInCollection &&
        !HasInjectAttribute &&
        !HasServiceKeyAttribute &&
        !HasFromKeyedServicesAttribute &&
        !HasDefaultValue;
}
