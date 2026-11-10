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
        ImmutableEquatableSet<AsyncInitService>? AsyncInitServices);

    /// <summary>
    /// A (type, key) pair registered as <c>Task&lt;T&gt;</c> only, because its registration is async-init.
    /// </summary>
    /// <param name="TypeName">The fully qualified service or implementation type name.</param>
    /// <param name="Key">The registration key expression, or <see langword="null"/> for a non-keyed registration.</param>
    private readonly record struct AsyncInitService(string TypeName, string? Key);

    /// <summary>
    /// Tag-grouped register output data including registrations and wrapper entries.
    /// </summary>
    private sealed record class RegisterTagGroup(
        ImmutableEquatableArray<string> Tags,
        ImmutableEquatableArray<RegisterEntry> Registrations,
        ImmutableEquatableArray<LazyRegistrationEntry> LazyEntries,
        ImmutableEquatableArray<FuncRegistrationEntry> FuncEntries,
        ImmutableEquatableArray<KvpRegistrationEntry> KvpEntries);
}
