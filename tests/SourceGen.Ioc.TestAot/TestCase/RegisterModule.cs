using Microsoft.Extensions.DependencyInjection;
using SourceGen.Ioc.TestCase;

namespace SourceGen.Ioc.TestAot.TestCase;

/// <summary>
/// Module that generates IServiceCollection extension methods for MS.Extensions.DI integration testing.
/// </summary>
[IocImportModule<TestCaseModule>]
[IocContainer]
public sealed partial class RegisterModule;
