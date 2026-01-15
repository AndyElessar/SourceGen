namespace IocSample;

public interface IFactoryService;

[IocRegister(RegisterAllInterfaces = true, Factory = nameof(Factory.Create), Key = "Test")]
internal sealed class FactoryService : IFactoryService
{
}

public sealed class Factory
{
    public static IFactoryService Create()
    {
        return new FactoryService();
    }
}

[IocRegister(Instance = nameof(Instance))]
internal sealed class InstanceService : IFactoryService
{
    public static readonly InstanceService Instance = new();
}
