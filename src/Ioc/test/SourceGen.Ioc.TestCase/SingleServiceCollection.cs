namespace SourceGen.Ioc.TestCase;

#pragma warning disable SGIOC011 // Duplicated Registration Detected: both containers register the same services

// Services that are never requested as a collection in this compilation, so the containers generate no
// collection resolver for them; collections of them are requested only by the AOT tests (another compilation).

/// <summary>Service with a single non-keyed registration.</summary>
public interface ISingleCollectionService;

internal sealed class SingleCollectionService : ISingleCollectionService;

/// <summary>Service with one non-keyed and one keyed registration.</summary>
public interface IKeyedSingleCollectionService
{
    string Name { get; }
}

internal sealed class DefaultKeyedSingleCollectionService : IKeyedSingleCollectionService
{
    public string Name => "Default";
}

internal sealed class AlphaKeyedSingleCollectionService : IKeyedSingleCollectionService
{
    public string Name => "Alpha";
}

/// <summary>Service with two non-keyed registrations, which always get a collection resolver.</summary>
public interface IMultiCollectionService;

internal sealed class FirstMultiCollectionService : IMultiCollectionService;

internal sealed class SecondMultiCollectionService : IMultiCollectionService;

/// <summary>Async-init service, which collections never contain.</summary>
public interface IAsyncSingleCollectionService;

internal sealed class AsyncSingleCollectionService : IAsyncSingleCollectionService
{
    [IocInject]
    public Task InitializeAsync() => Task.CompletedTask;
}

/// <summary>Service with a single keyed registration, consumed as a keyed collection.</summary>
public interface IKeyedConsumedCollectionService;

internal sealed class KeyedConsumedCollectionService : IKeyedConsumedCollectionService;

/// <summary>Consumer of a keyed collection, which the container resolves through <c>GetKeyedServices</c>.</summary>
public sealed class KeyedSingleCollectionConsumer([IocInject(Key = "alpha")] IEnumerable<IKeyedConsumedCollectionService> services)
{
    public IEnumerable<IKeyedConsumedCollectionService> Services { get; } = services;
}

[IocRegisterFor<SingleCollectionService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(ISingleCollectionService)])]
[IocRegisterFor<DefaultKeyedSingleCollectionService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedSingleCollectionService)])]
[IocRegisterFor<AlphaKeyedSingleCollectionService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedSingleCollectionService)], Key = "alpha")]
[IocRegisterFor<FirstMultiCollectionService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMultiCollectionService)])]
[IocRegisterFor<SecondMultiCollectionService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMultiCollectionService)])]
[IocRegisterFor<AsyncSingleCollectionService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IAsyncSingleCollectionService)])]
[IocRegisterFor<KeyedConsumedCollectionService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedConsumedCollectionService)], Key = "alpha")]
[IocRegisterFor<KeyedSingleCollectionConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class SingleServiceCollectionModule;

[IocRegisterFor<SingleCollectionService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(ISingleCollectionService)])]
[IocRegisterFor<DefaultKeyedSingleCollectionService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedSingleCollectionService)])]
[IocRegisterFor<AlphaKeyedSingleCollectionService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedSingleCollectionService)], Key = "alpha")]
[IocRegisterFor<FirstMultiCollectionService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMultiCollectionService)])]
[IocRegisterFor<SecondMultiCollectionService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMultiCollectionService)])]
[IocRegisterFor<AsyncSingleCollectionService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IAsyncSingleCollectionService)])]
[IocRegisterFor<KeyedConsumedCollectionService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedConsumedCollectionService)], Key = "alpha")]
[IocRegisterFor<KeyedSingleCollectionConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true, UseSwitchStatement = true)]
public sealed partial class SingleServiceCollectionSwitchModule;
