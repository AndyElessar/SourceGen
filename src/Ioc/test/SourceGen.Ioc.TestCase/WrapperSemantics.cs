namespace SourceGen.Ioc.TestCase;

/// <summary>Decorated service resolved through <c>Lazy&lt;T&gt;</c> and <c>Func&lt;T&gt;</c>.</summary>
public interface IWrappedService
{
    /// <summary>The wrapped service, or <see langword="null"/> for the undecorated implementation.</summary>
    IWrappedService? Inner { get; }
}

public sealed class WrappedService : IWrappedService
{
    public IWrappedService? Inner => null;
}

public sealed class WrappedServiceDecorator(IWrappedService inner) : IWrappedService
{
    public IWrappedService? Inner => inner;
}

/// <summary>Transient service resolved through <c>Lazy&lt;T&gt;</c>.</summary>
public interface ITransientWrappedService;

public sealed class TransientWrappedService : ITransientWrappedService;

/// <summary>Service registered both non-keyed and keyed, resolved through <c>Lazy&lt;T&gt;</c>.</summary>
public interface IKeyedWrappedService;

public sealed class KeyedWrappedService : IKeyedWrappedService;

public sealed class WrapperSemanticsConsumer(
    Lazy<IWrappedService> decoratedLazy,
    Func<IWrappedService> decoratedFactory,
    Lazy<ITransientWrappedService> transientLazy,
    [FromKeyedServices("keyed")] Lazy<IKeyedWrappedService> keyedLazy,
    IEnumerable<Lazy<IKeyedWrappedService>> allKeyedWrapped)
{
    public Lazy<IWrappedService> DecoratedLazy => decoratedLazy;
    public Func<IWrappedService> DecoratedFactory => decoratedFactory;
    public Lazy<ITransientWrappedService> TransientLazy => transientLazy;
    public Lazy<IKeyedWrappedService> KeyedLazy => keyedLazy;
    public IEnumerable<Lazy<IKeyedWrappedService>> AllKeyedWrapped => allKeyedWrapped;
}

[IocRegisterFor<WrappedService>(
    ServiceLifetime.Singleton,
    ServiceTypes = [typeof(IWrappedService)],
    Decorators = [typeof(WrappedServiceDecorator)])]
[IocRegisterFor<TransientWrappedService>(ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientWrappedService)])]
[IocRegisterFor<KeyedWrappedService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedWrappedService)])]
[IocRegisterFor<KeyedWrappedService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedWrappedService)], Key = "keyed")]
[IocRegisterFor<WrapperSemanticsConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class WrapperSemanticsModule;
