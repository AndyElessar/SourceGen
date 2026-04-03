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

internal static class FactoryServiceFactory
{
    public static IFactoryService Create(IFactoryDep dep) =>
        new FactoryService { CreatedBy = nameof(FactoryServiceFactory), DepName = dep.Label };
}

[IocRegisterFor<FactoryDep>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFactoryDep)])]
[IocRegisterFor<FactoryService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFactoryService)], Factory = nameof(FactoryServiceFactory.Create))]
[IocRegisterFor<InstanceService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IInstanceService)], Instance = nameof(InstanceService.Default))]
[IocContainer(ExplicitOnly = true)]
public sealed partial class FactoryModule;
