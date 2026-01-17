[assembly: IocRegisterDefaults<IFactoryService>(ServiceLifetime.Transient, Factory = nameof(Factory.Create))]

namespace IocSample;

public interface IFactoryService;

[IocRegister<IFactoryService>(Key = "Test")]
internal sealed class FactoryService : IFactoryService
{
}

public sealed class Factory
{
    public static IFactoryService Create(IServiceProvider sp, [ServiceKey] string key, IInstance inst)
    {
        return new FactoryService();
    }
}

[IocRegister<IFactoryService>(Factory = nameof(Create))]
internal class FactoryService2 : IFactoryService
{
    public static IFactoryService Create() => new FactoryService2();
}

public interface IInstance;
[IocRegister(Instance = nameof(Instance))]
internal sealed class InstanceService : IInstance
{
    public static readonly InstanceService Instance = new();
}
