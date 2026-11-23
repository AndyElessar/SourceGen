namespace SourceGen.Ioc;

/// <summary>Options for all containers and <c>Add{Name}</c> methods that SourceGen.Ioc generates.</summary>
public static class IocContainerGlobalOptions
{
    /// <summary>
    /// Gets or sets the callback that receives the exceptions of disposal that cannot be thrown to a caller: a disposal that a continuation does after
    /// <c>Dispose</c> returned, and a disposal after a failed creation. It can be called on any thread, also concurrently.
    /// When it is <see langword="null"/>, these exceptions are discarded.
    /// </summary>
    public static Action<Exception>? OnDisposeException { get; set; }
}
