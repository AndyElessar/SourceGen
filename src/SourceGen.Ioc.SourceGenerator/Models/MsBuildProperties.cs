namespace SourceGen.Ioc.SourceGenerator.Models;

internal sealed record MsBuildProperties(
    string? RootNamespace,
    string? CustomIocName,
    ServiceLifetime? DefaultLifetime,
    IocFeatures Features);
