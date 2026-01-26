namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Thread safety strategy for singleton and scoped service resolution.
/// </summary>
internal enum ThreadSafeStrategy
{
    /// <summary>
    /// No thread safety. Direct field assignment without synchronization.
    /// </summary>
    None = 0,

    /// <summary>
    /// Use lock statement with double-checked locking pattern.
    /// </summary>
    Lock = 1,

    /// <summary>
    /// Use SemaphoreSlim with double-checked locking pattern. (Default)
    /// </summary>
    SemaphoreSlim = 2,

    /// <summary>
    /// Use SpinLock with double-checked locking pattern.
    /// </summary>
    SpinLock = 4,
}

/// <summary>
/// Represents a container class marked with [IocContainer].
/// </summary>
/// <param name="ContainerTypeName">Fully qualified type name of the container class.</param>
/// <param name="ContainerNamespace">Namespace of the container class.</param>
/// <param name="ClassName">Simple class name without namespace.</param>
/// <param name="ResolveIServiceCollection">Whether to support external IServiceProvider fallback.</param>
/// <param name="ExplicitOnly">Whether to only include explicitly marked registrations.</param>
/// <param name="IncludeTags">Tags to filter services by. When non-empty, only services with matching tags are included.</param>
/// <param name="UseSwitchStatement">Whether to use switch statement instead of FrozenDictionary.</param>
/// <param name="ThreadSafeStrategy">Thread safety strategy for singleton/scoped service resolution.</param>
/// <param name="ImportedModules">Types of imported module containers.</param>
/// <param name="ExplicitRegistrations">Registrations explicitly marked on the container class (for ExplicitOnly mode).</param>
internal sealed record class ContainerModel(
    string ContainerTypeName,
    string ContainerNamespace,
    string ClassName,
    bool ResolveIServiceCollection,
    bool ExplicitOnly,
    ImmutableEquatableArray<string> IncludeTags,
    bool UseSwitchStatement,
    ThreadSafeStrategy ThreadSafeStrategy,
    ImmutableEquatableArray<TypeData> ImportedModules,
    ImmutableEquatableArray<RegistrationData> ExplicitRegistrations);
