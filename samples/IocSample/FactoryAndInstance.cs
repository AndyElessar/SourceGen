namespace IocSample;

public interface IFactoryService;

[IocRegister<IFactoryService>]
internal sealed class FactoryService : IFactoryService
{
}

[IocRegisterDefaults<IFactoryService>(ServiceLifetime.Scoped, Factory = nameof(Factory.Create))]
public sealed class Factory
{
    public static IFactoryService Create(IServiceProvider sp, IInstance inst)
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

public interface IGenericFactoryService<T>;
public interface IWrapper<T>;

internal sealed class GenericFactoryService<T> : IGenericFactoryService<IWrapper<T>>
{
}

[IocRegisterDefaults(
    typeof(IGenericFactoryService<>), 
    ServiceLifetime.Singleton, 
    Factory = nameof(GenericFactory.Create))]
internal static class GenericFactory
{
    [IocDiscover(typeof(IGenericFactoryService<IWrapper<decimal>>))]
    [IocGenericFactory(typeof(IGenericFactoryService<IWrapper<int>>), typeof(int))]
    public static IGenericFactoryService<IWrapper<T>> Create<T>() => new GenericFactoryService<T>();
}
