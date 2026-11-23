namespace SourceGen.Ioc;

/// <summary>Which services the container creates when the root or a scope is created.</summary>
[Flags]
public enum EagerResolveOptions
{
    /// <summary>Each service is created at its first request.</summary>
    None = 0,

    /// <summary>The constructor of the root creates all singleton services.</summary>
    Singleton = 1,

    /// <summary>The creation of a scope creates all scoped services.</summary>
    Scoped = 1 << 1,

    /// <summary>Both <see cref="Singleton"/> and <see cref="Scoped"/>.</summary>
    SingletonAndScoped = Singleton | Scoped,
}
