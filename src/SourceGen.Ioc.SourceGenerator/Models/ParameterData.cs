namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents parameter information.
/// </summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Type">The type data of the parameter.</param>
/// <param name="IsOptional">Whether this parameter is optional (has default value or is nullable).</param>
/// <param name="ServiceKey">The key for keyed service resolution from [FromKeyedServices] or [Inject] attribute if present.</param>
/// <param name="HasInjectAttribute">Whether this parameter has [Inject] attribute (not [FromKeyedServices], which MS.DI handles automatically).</param>
/// <param name="HasServiceKeyAttribute">Whether this parameter has [ServiceKey] attribute from Microsoft.Extensions.DependencyInjection.</param>
internal sealed record class ParameterData(
    string Name,
    TypeData Type,
    bool IsOptional = false,
    string? ServiceKey = null,
    bool HasInjectAttribute = false,
    bool HasServiceKeyAttribute = false);
