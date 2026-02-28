using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc.TestCase;

/// <summary>Keyed service interface for testing keyed service resolution.</summary>
public interface IKeyedService
{
    string Key { get; }
}

internal sealed class KeyedServiceA : IKeyedService
{
    public string Key => "A";
}

internal sealed class KeyedServiceB : IKeyedService
{
    public string Key => "B";
}

internal sealed class KeyedScopedService : IKeyedService
{
    public string Key => "Scoped";
}

[IocRegisterFor<KeyedServiceA>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = "A")]
[IocRegisterFor<KeyedServiceB>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = "B")]
[IocRegisterFor<KeyedScopedService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IKeyedService)], Key = "Scoped")]
[IocContainer(ExplicitOnly = true)]
public sealed partial class KeyedModule;
