namespace SourceGen.Ioc;

/// <summary>
/// Defines the eager resolution options for services in the generated IoC container.
/// </summary>
[Flags]
public enum EagerResolveOptions
{
    /// <summary>
    /// Do not eagerly resolve any services.
    /// </summary>
    None = 0,

    /// <summary>
    /// Eagerly resolve all singleton services when root container is created.
    /// </summary>
    Singleton = 1,

    /// <summary>
    /// Eagerly resolve all scoped services when scope is created.
    /// </summary>
    Scoped = 1 << 1,

    /// <summary>
    /// Eagerly resolve both singleton and scoped services.
    /// </summary>
    SingletonAndScoped = Singleton | Scoped,
}
