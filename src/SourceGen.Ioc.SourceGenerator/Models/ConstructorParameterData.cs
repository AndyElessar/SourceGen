namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents constructor parameter information for a type.
/// </summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Type">The type data of the parameter.</param>
/// <param name="IsServiceParameter">Whether this parameter represents the service type being decorated.</param>
/// <param name="IsOptional">Whether this parameter is optional (has default value or is nullable).</param>
internal sealed record class ConstructorParameterData(
    string Name,
    TypeData Type,
    bool IsServiceParameter = false,
    bool IsOptional = false);
