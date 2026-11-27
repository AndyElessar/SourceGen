namespace SourceGen.Ioc.TestAot.Container;

/// <summary>A disposable static object: neither output disposes it (Semantics.spec, "Factory and instance").</summary>
public sealed class StaticResource : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}

/// <summary>The service of an instance that a test can make <see langword="null"/>.</summary>
public sealed class Marker;

/// <summary>Instance registrations (Annotations.spec, "Instance registration").</summary>
public static class AotInstances
{
    public const string MarkerKey = "marker";

    private static readonly Marker DefaultMarker = new();

    [IocInstance]
    public static readonly StaticResource Resource = new();

    /// <summary>
    /// Makes <see cref="Current"/> <see langword="null"/> on this thread. The service collection extension reads the
    /// member when it registers it, so the member is not <see langword="null"/> by default.
    /// </summary>
    [ThreadStatic]
    public static bool ReturnNull;

    [IocInstance(Key = MarkerKey)]
    public static Marker Current => ReturnNull ? null! : DefaultMarker;
}
