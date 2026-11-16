namespace SourceGen.Ioc.TestCase;

/// <summary>Service with two registrations in <see cref="NonIntegratedPluginModule"/>, which gets a collection resolver.</summary>
public interface INonIntegratedModulePlugin;

internal sealed class FirstNonIntegratedModulePlugin : INonIntegratedModulePlugin;

internal sealed class SecondNonIntegratedModulePlugin : INonIntegratedModulePlugin;

/// <summary>
/// Module without <c>IntegrateServiceProvider</c>, imported (from another assembly) by a container that integrates the
/// fallback provider: collections resolved through the importer merge the importer's fallback services.
/// </summary>
[IocRegisterFor<FirstNonIntegratedModulePlugin>(ServiceLifetime.Singleton, ServiceTypes = [typeof(INonIntegratedModulePlugin)])]
[IocRegisterFor<SecondNonIntegratedModulePlugin>(ServiceLifetime.Singleton, ServiceTypes = [typeof(INonIntegratedModulePlugin)])]
[IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
public sealed partial class NonIntegratedPluginModule;
