namespace SourceGen.Ioc.TestCase;

/// <summary>Interface for factory-created service.</summary>
public interface IFactoryService
{
    string CreatedBy { get; }

    /// <summary>Label from the injected <see cref="IFactoryDep"/> dependency, proving the dep was resolved.</summary>
    string DepName { get; }
}

internal sealed class FactoryService : IFactoryService
{
    public string CreatedBy { get; init; } = string.Empty;
    public string DepName { get; init; } = string.Empty;
}

/// <summary>Dependency injected into the factory method to prove the generator resolves factory parameters.</summary>
public interface IFactoryDep
{
    string Label { get; }
}

internal sealed class FactoryDep : IFactoryDep
{
    public string Label => nameof(FactoryDep);
}

/// <summary>Interface for instance-based service.</summary>
public interface IInstanceService
{
    string Name { get; }
}

internal sealed class InstanceService : IInstanceService
{
    public static readonly InstanceService Default = new();
    public string Name => nameof(InstanceService);
}

/// <summary>Decorator declared on the instance registration; decorators are not applied to instance registrations.</summary>
public sealed class InstanceServiceDecorator(IInstanceService inner) : IInstanceService
{
    public string Name => $"{nameof(InstanceServiceDecorator)}({inner.Name})";
}

/// <summary>Consumer resolving the instance registration directly and through Lazy/Func wrappers.</summary>
public sealed class InstanceServiceConsumer(IInstanceService direct, Lazy<IInstanceService> lazy, Func<IInstanceService> factory)
{
    public IInstanceService Direct => direct;
    public Lazy<IInstanceService> Lazy => lazy;
    public Func<IInstanceService> Factory => factory;
}

internal static class FactoryServiceFactory
{
    public static IFactoryService Create(IFactoryDep dep) =>
        new FactoryService { CreatedBy = nameof(FactoryServiceFactory), DepName = dep.Label };
}

/// <summary>Interface for a factory-created service wrapped with a decorator.</summary>
public interface IDecoratedFactoryService
{
    string Describe();
}

public sealed class DecoratedFactoryService : IDecoratedFactoryService
{
    public string Describe() => nameof(DecoratedFactoryService);
}

public sealed class DecoratedFactoryServiceDecorator(IDecoratedFactoryService inner) : IDecoratedFactoryService
{
    public IDecoratedFactoryService Inner => inner;

    public string Describe() => $"{nameof(DecoratedFactoryServiceDecorator)}({inner.Describe()})";
}

internal static class DecoratedFactoryServiceFactory
{
    public static IDecoratedFactoryService Create() => new DecoratedFactoryService();
}

[IocRegisterFor<FactoryDep>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFactoryDep)])]
[IocRegisterFor<FactoryService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFactoryService)], Factory = nameof(FactoryServiceFactory.Create))]
[IocRegisterFor<DecoratedFactoryService>(
    ServiceLifetime.Singleton,
    ServiceTypes = [typeof(IDecoratedFactoryService)],
    Factory = nameof(DecoratedFactoryServiceFactory.Create),
    Decorators = [typeof(DecoratedFactoryServiceDecorator)])]
[IocRegisterFor<InstanceService>(
    ServiceLifetime.Singleton,
    ServiceTypes = [typeof(IInstanceService)],
    Instance = nameof(InstanceService.Default),
    Decorators = [typeof(InstanceServiceDecorator)])]
[IocRegisterFor<InstanceServiceConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class FactoryModule;
