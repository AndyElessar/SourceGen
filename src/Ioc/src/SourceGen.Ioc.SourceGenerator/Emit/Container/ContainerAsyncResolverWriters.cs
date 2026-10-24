namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the async routing resolver method name by appending "Async" to the sync method name.
    /// </summary>
    private static string GetAsyncResolverMethodName(string syncMethodName)
        => syncMethodName + "Async";

    /// <summary>
    /// Returns the async creation method name (e.g. "CreateFooBarAsync" from "GetFooBar").
    /// </summary>
    private static string GetAsyncCreateMethodName(string syncMethodName)
    {
        if(syncMethodName.Length > 3 && syncMethodName.StartsWith("Get", StringComparison.Ordinal))
            return "Create" + syncMethodName[3..] + "Async";
        return syncMethodName + "_CreateAsync";
    }

    /// <summary>
    /// Returns the effective thread-safety strategy for a registration.
    /// Async-init services auto-upgrade async-incompatible strategies to <see cref="ThreadSafeStrategy.SemaphoreSlim"/>.
    /// </summary>
    private static ThreadSafeStrategy GetEffectiveThreadSafeStrategy(
        ThreadSafeStrategy strategy,
        bool isAsyncInit)
    {
        if(!isAsyncInit)
            return strategy;

        return strategy is ThreadSafeStrategy.None ? ThreadSafeStrategy.None : ThreadSafeStrategy.SemaphoreSlim;
    }

}
