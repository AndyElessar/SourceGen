namespace SourceGen.Ioc.TestCase;

/// <summary>
/// Main module that aggregates all test case modules.
/// Import this module to get all test services registered.
/// </summary>
[IocImportModule<BasicModule>]
[IocImportModule<KeyedModule>]
[IocImportModule<InjectionModule>]
[IocImportModule<DecoratorModule>]
[IocImportModule<OpenGenericModule>]
[IocContainer(ExplicitOnly = true)]
public sealed partial class TestCaseModule;
