using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc.TestCase;

#region Lifetime Services

/// <summary>Singleton service interface.</summary>
public interface ISingletonService
{
    Guid InstanceId { get; }
}

/// <summary>Scoped service interface.</summary>
public interface IScopedService
{
    Guid InstanceId { get; }
}

/// <summary>Transient service interface.</summary>
public interface ITransientService
{
    Guid InstanceId { get; }
}

internal sealed class SingletonService : ISingletonService
{
    public Guid InstanceId { get; } = Guid.NewGuid();
}

internal sealed class ScopedService : IScopedService
{
    public Guid InstanceId { get; } = Guid.NewGuid();
}

internal sealed class TransientService : ITransientService
{
    public Guid InstanceId { get; } = Guid.NewGuid();
}

#endregion

[IocRegisterFor<SingletonService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ISingletonService)])]
[IocRegisterFor<ScopedService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
[IocRegisterFor<TransientService>(ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
[IocContainer(ExplicitOnly = true)]
public sealed partial class BasicModule;
