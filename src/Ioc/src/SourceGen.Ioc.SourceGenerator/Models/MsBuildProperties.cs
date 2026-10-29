namespace SourceGen.Ioc.SourceGenerator.Models;

internal sealed record class MsBuildProperties(
    string? RootNamespace,
    string? CustomIocName,
    IocFeatures Features);
