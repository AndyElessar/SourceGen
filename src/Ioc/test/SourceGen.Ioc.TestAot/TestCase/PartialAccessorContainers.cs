namespace SourceGen.Ioc.TestAot.TestCase;

public interface IAccessorModuleService;

internal sealed class AccessorModuleService : IAccessorModuleService;

public interface IAccessorLocalService;

internal sealed class AccessorLocalService : IAccessorLocalService;

/// <summary>Module whose service is resolved only through the partial accessors of <see cref="PartialAccessorContainer"/>.</summary>
[IocRegisterFor<AccessorModuleService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IAccessorModuleService)])]
[IocRegisterFor<AccessorModuleService>(ServiceLifetime.Singleton, Key = "keyed", ServiceTypes = [typeof(IAccessorModuleService)])]
[IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
public sealed partial class AccessorModule;

/// <summary>
/// Standalone container whose partial accessors resolve services of imported modules (of this and another assembly)
/// and wrappers and collections of its own services.
/// </summary>
[IocImportModule<AccessorModule>]
[IocImportModule<TestCaseModule>]
[IocRegisterFor<AccessorLocalService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IAccessorLocalService)])]
[IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
public sealed partial class PartialAccessorContainer
{
    public partial IAccessorModuleService GetModuleService();

    public partial IAccessorModuleService? TryGetModuleService();

    [IocInject("keyed")]
    public partial IAccessorModuleService KeyedModuleService { get; }

    public partial Task<IAccessorModuleService> GetModuleServiceAsync();

    public partial ISingletonService GetCrossAssemblyModuleService();

    public partial IAccessorLocalService GetLocalService();

    public partial Lazy<IAccessorLocalService> LazyLocalService { get; }

    public partial Func<IAccessorLocalService> GetLocalServiceFactory();

    public partial IEnumerable<IAccessorLocalService> GetLocalServices();

    public partial IAccessorLocalService[] LocalServiceArray { get; }
}
