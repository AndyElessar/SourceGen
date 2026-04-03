namespace SourceGen.Ioc.TestAot.TestCase;

/// <summary>
/// Composite standalone container that wires <see cref="InjectionModule"/> and
/// <see cref="AsyncInjectionModule"/> together.  Used only for verifying that
/// the composite resolver builds without errors; async-init service access is
/// exercised directly on <see cref="AsyncInjectionModule"/> (with
/// <see cref="InjectionModule"/> as fallback provider).
/// </summary>
[IocImportModule<InjectionModule>]
[IocImportModule<AsyncInjectionModule>]
[IocContainer(
    ExplicitOnly = true,
    ThreadSafeStrategy = ThreadSafeStrategy.SemaphoreSlim,
    EagerResolveOptions = EagerResolveOptions.None)]
public sealed partial class AsyncInjectionContainer;
