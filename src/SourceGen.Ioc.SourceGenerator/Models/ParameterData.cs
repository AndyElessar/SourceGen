namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents parameter information.
/// </summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Type">The type data of the parameter.</param>
/// <param name="IsOptional">Whether this parameter is optional (has default value or is nullable).</param>
internal sealed record class ParameterData(
    string Name,
    TypeData Type,
    bool IsOptional = false);
