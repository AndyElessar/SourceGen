namespace IocSample;

public interface IFactoryService;

[IoCRegister(RegisterAllInterfaces = true, Factory = nameof(Factory.Create), Key = "Test")]
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

[IoCRegister(Instance = nameof(Instance))]
internal sealed class InstanceService : IFactoryService
{
    public static readonly InstanceService Instance = new();
}