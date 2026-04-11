namespace SourceGen.Ioc.SourceGenerator.Models;

internal sealed record MsBuildProperties(
    string? RootNamespace,
    string? CustomIocName,
    IocFeatures Features);
