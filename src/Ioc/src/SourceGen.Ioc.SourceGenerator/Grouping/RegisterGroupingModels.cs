namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
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