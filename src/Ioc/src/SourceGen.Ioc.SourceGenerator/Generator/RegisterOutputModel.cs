namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Pre-computed output flags for a single registration in register output generation.
    /// </summary>
    private readonly record struct RegisterOutputEntry(
        ServiceRegistrationModel Registration,
        bool HasFactory,
        bool HasInstance,
        bool HasClosedDecorators,
        bool NeedsFactoryConstruction,
        bool HasAsyncInjectionMembers,
        bool ShouldForwardServiceType);

    /// <summary>
    /// Tag-grouped register output data including registrations and wrapper entries.
    /// </summary>
    private sealed record class RegisterTagGroup(
        ImmutableEquatableArray<string> Tags,
        ImmutableEquatableArray<RegisterOutputEntry> Registrations,
        ImmutableEquatableArray<LazyRegistrationEntry> LazyEntries,
        ImmutableEquatableArray<FuncRegistrationEntry> FuncEntries,
        ImmutableEquatableArray<KvpRegistrationEntry> KvpEntries);

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