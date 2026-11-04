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
    /// Returns the sync creation (slow path) method name (e.g. "CreateFooBar" from "GetFooBar").
    /// </summary>
    private static string GetCreateMethodName(string syncMethodName)
    {
        if(syncMethodName.Length > 3 && syncMethodName.StartsWith("Get", StringComparison.Ordinal))
            return "Create" + syncMethodName[3..];
        return syncMethodName + "_Create";
    }

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
    /// Returns the async first-initialization (slow path) method name (e.g. "InitFooBarAsync" from "GetFooBar").
    /// </summary>
    private static string GetAsyncInitMethodName(string syncMethodName)
    {
        if(syncMethodName.Length > 3 && syncMethodName.StartsWith("Get", StringComparison.Ordinal))
            return "Init" + syncMethodName[3..] + "Async";
        return syncMethodName + "_InitAsync";
    }

    /// <summary>
    /// Returns the effective thread-safety strategy for a registration.
    /// Async-init services map <see cref="ThreadSafeStrategy.SpinLock"/> and <see cref="ThreadSafeStrategy.CompareExchange"/>
    /// to <see cref="ThreadSafeStrategy.Lock"/>: the lock body runs user initialization code (unsuitable for spinning),
    /// and CAS would start duplicate initializations.
    /// </summary>
    private static ThreadSafeStrategy GetEffectiveThreadSafeStrategy(
        ThreadSafeStrategy strategy,
        bool isAsyncInit)
    {
        if(!isAsyncInit)
            return strategy;

        return strategy is ThreadSafeStrategy.SpinLock or ThreadSafeStrategy.CompareExchange ? ThreadSafeStrategy.Lock : strategy;
    }

}
