namespace SourceGen.Ioc.TestCase;

/// <summary>First interface for multi-interface registration testing.</summary>
public interface IServiceA
{
    string NameA { get; }
}

/// <summary>Second interface for multi-interface registration testing.</summary>
public interface IServiceB
{
    string NameB { get; }
}

public sealed class MultiInterfaceService : IServiceA, IServiceB
{
    public string NameA => nameof(IServiceA);
    public string NameB => nameof(IServiceB);
}

[IocRegisterFor<MultiInterfaceService>(ServiceLifetime.Singleton, RegisterAllInterfaces = true)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class RegisterAllInterfacesModule;
