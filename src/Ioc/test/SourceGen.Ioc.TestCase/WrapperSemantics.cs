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

/// <summary>Service created by a <c>Func&lt;string, T&gt;</c> from its name argument.</summary>
public interface INamedService
{
    string Name { get; }
    INamedService? Inner { get; }
    object? Key { get; }
}

public sealed class NamedService(string name) : INamedService
{
    public string Name => name;
    public INamedService? Inner => null;
    public object? Key => null;
}

public sealed class NamedServiceDecorator(INamedService inner) : INamedService
{
    public string Name => inner.Name;
    public INamedService? Inner => inner;
    public object? Key => inner.Key;
}

public sealed class KeyedNamedService(string name, [ServiceKey] object? key) : INamedService
{
    public string Name => name;
    public INamedService? Inner => null;
    public object? Key => key;
}

public sealed class FuncWithArgumentsConsumer(
    Func<string, INamedService> factory,
    [FromKeyedServices("keyed")] Func<string, INamedService> keyedFactory)
{
    public Func<string, INamedService> Factory => factory;
    public Func<string, INamedService> KeyedFactory => keyedFactory;
}

[IocRegisterFor<NamedService>(
    ServiceLifetime.Singleton,
    ServiceTypes = [typeof(INamedService)],
    Decorators = [typeof(NamedServiceDecorator)])]
[IocRegisterFor<KeyedNamedService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(INamedService)], Key = "keyed")]
// Deliberately a Func with arguments of a singleton, to test that each call creates a new instance
#pragma warning disable SGIOC033
[IocRegisterFor<FuncWithArgumentsConsumer>(ServiceLifetime.Transient)]
#pragma warning restore SGIOC033
[IocRegisterFor<WrappedService>(
    ServiceLifetime.Singleton,
    ServiceTypes = [typeof(IWrappedService)],
    Decorators = [typeof(WrappedServiceDecorator)])]
[IocRegisterFor<TransientWrappedService>(ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientWrappedService)])]
[IocRegisterFor<KeyedWrappedService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedWrappedService)])]
[IocRegisterFor<KeyedWrappedService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedWrappedService)], Key = "keyed")]
[IocRegisterFor<WrapperSemanticsConsumer>(ServiceLifetime.Transient)]
// Not eager: NamedService takes its name from the Func argument and cannot be resolved directly
[IocContainer(ExplicitOnly = true, EagerResolveOptions = EagerResolveOptions.None)]
public sealed partial class WrapperSemanticsModule;
