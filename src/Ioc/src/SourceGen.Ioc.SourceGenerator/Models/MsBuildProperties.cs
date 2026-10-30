namespace SourceGen.Ioc;

internal sealed record class MsBuildProperties(
    string? RootNamespace,
    string? CustomIocName,
    IocFeatures Features);
