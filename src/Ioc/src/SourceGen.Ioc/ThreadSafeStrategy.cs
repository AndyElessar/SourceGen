namespace SourceGen.Ioc;

/// <summary>How the container protects the first creation of a singleton or scoped service.</summary>
public enum ThreadSafeStrategy
{
    /// <summary>No protection. Use it only when one thread uses the container.</summary>
    None = 0,

    /// <summary>A double check with a lock. The instance is created one time.</summary>
    Lock = 1,

    /// <summary>No lock. A concurrent first resolution can create more than one instance; the first one stays, and the others are disposed.</summary>
    CompareExchange = 1 << 1,
}
