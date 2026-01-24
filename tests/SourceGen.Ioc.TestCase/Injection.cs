using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc.TestCase;

/// <summary>Dependency interface for injection testing.</summary>
public interface IInjectionDependency
{
    string Name { get; }
}

internal sealed class InjectionDependency : IInjectionDependency
{
    public string Name => nameof(InjectionDependency);
}

/// <summary>Service with property injection.</summary>
public interface IPropertyInjectedService
{
    IInjectionDependency? PropertyDependency { get; }
}

/// <summary>Service with field injection.</summary>
public interface IFieldInjectedService
{
    IInjectionDependency? GetFieldDependency();
}

/// <summary>Service with method injection.</summary>
public interface IMethodInjectedService
{
    IInjectionDependency? MethodDependency { get; }
    bool IsInitialized { get; }
}

/// <summary>Service with constructor injection (baseline).</summary>
public interface IConstructorInjectedService
{
    IInjectionDependency ConstructorDependency { get; }
}

internal sealed class PropertyInjectedService : IPropertyInjectedService
{
    [IocInject]
    public IInjectionDependency? PropertyDependency { get; set; }
}

internal sealed class FieldInjectedService : IFieldInjectedService
{
    [IocInject]
    internal IInjectionDependency? _fieldDependency;

    public IInjectionDependency? GetFieldDependency() => _fieldDependency;
}

internal sealed class MethodInjectedService : IMethodInjectedService
{
    public IInjectionDependency? MethodDependency { get; private set; }
    public bool IsInitialized { get; private set; }

    [IocInject]
    internal void Initialize(IInjectionDependency dependency)
    {
        MethodDependency = dependency;
        IsInitialized = true;
    }
}

internal sealed class ConstructorInjectedService(IInjectionDependency dependency) : IConstructorInjectedService
{
    public IInjectionDependency ConstructorDependency => dependency;
}

[IocRegisterFor<InjectionDependency>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IInjectionDependency)])]
[IocRegisterFor<PropertyInjectedService>(ServiceLifetime.Transient, ServiceTypes = [typeof(IPropertyInjectedService)])]
[IocRegisterFor<FieldInjectedService>(ServiceLifetime.Transient, ServiceTypes = [typeof(IFieldInjectedService)])]
[IocRegisterFor<MethodInjectedService>(ServiceLifetime.Transient, ServiceTypes = [typeof(IMethodInjectedService)])]
[IocRegisterFor<ConstructorInjectedService>(ServiceLifetime.Transient, ServiceTypes = [typeof(IConstructorInjectedService)])]
[IocContainer(ExplicitOnly = true)]
public sealed partial class InjectionModule;
