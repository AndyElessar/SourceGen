namespace SourceGen.Ioc;

/// <summary>
/// Defines thread safety strategies for <see cref="IIocContainer{T}"/>.
/// </summary>
public enum ThreadSafeStrategy
{
  /// <summary>
  /// No thread safety.
  /// </summary>
  None = 0,

  /// <summary>
  /// Use lock statement for thread safety.
  /// </summary>
  Lock = 1,

  /// <summary>
  /// Use SemaphoreSlim for thread safety.
  /// </summary>
  SemaphoreSlim = 1 << 1,

  /// <summary>
  /// Use SpinLock for thread safety.
  /// </summary>
  SpinLock = 1 << 2,
}
