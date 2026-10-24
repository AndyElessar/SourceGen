namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Top-level output model for register source generation.
    /// </summary>
    private sealed record class RegisterOutputModel(
        string MethodBaseName,
        string RootNamespace,
        string AssemblyName,
        ImmutableEquatableArray<RegisterTagGroup> TagGroups,
        ImmutableEquatableSet<string>? AsyncInitServiceTypes);
}