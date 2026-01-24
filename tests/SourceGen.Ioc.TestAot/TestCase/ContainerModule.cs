using Microsoft.Extensions.DependencyInjection;
using SourceGen.Ioc.TestCase;

namespace SourceGen.Ioc.TestAot.TestCase;

/// <summary>
/// Standalone container that implements IIocContainer for container behavior testing.
/// </summary>
/// <remarks>
/// Note: Open generic registrations from external assemblies (like TestCaseModule's OpenGenericModule)
/// are not automatically inherited due to [Conditional("SOURCEGEN")] on IoC attributes.
/// We must re-register the open generic here for cross-assembly scenarios.
/// </remarks>
[IocImportModule<TestCaseModule>]
[IocRegisterFor(typeof(GenericHandler<,>), ServiceLifetime.Transient, ServiceTypes = [typeof(IHandler<,>)])]
[IocContainer]
public sealed partial class ContainerModule;
