namespace SourceGen.Ioc.TestAot.TestCase;

#pragma warning disable SGIOC011 // Duplicated Registration Detected

/// <summary>
/// Container with UseSwitchStatement = true for testing the switch-statement resolution path.
/// Direct registrations are used instead of module imports because UseSwitchStatement is
/// ignored when imported modules are present (SGIOC020).
/// </summary>
[IocRegisterFor<SingletonService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ISingletonService)])]
[IocRegisterFor<ScopedService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
[IocRegisterFor<TransientService>(ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
[IocRegisterFor<WrapperCollectionConsumer>(ServiceLifetime.Transient)]
[IocContainer(UseSwitchStatement = true, ExplicitOnly = true)]
public sealed partial class SwitchStatementContainer;

/// <summary>Consumer of Lazy/Func wrapper collections, resolved through the switch-statement path.</summary>
public sealed class WrapperCollectionConsumer(
    IEnumerable<Lazy<ISingletonService>> lazyServices,
    IEnumerable<Func<ITransientService>> serviceFactories)
{
    public IEnumerable<Lazy<ISingletonService>> LazyServices { get; } = lazyServices;
    public IEnumerable<Func<ITransientService>> ServiceFactories { get; } = serviceFactories;
}
