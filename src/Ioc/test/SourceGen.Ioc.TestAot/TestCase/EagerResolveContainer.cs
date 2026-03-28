namespace SourceGen.Ioc.TestAot.TestCase;

/// <summary>
/// Container with EagerResolveOptions.SingletonAndScoped to verify eager resolution behavior.
/// Singletons are resolved at container construction; scoped services at scope creation.
/// </summary>
[IocImportModule<BasicModule>]
[IocContainer(EagerResolveOptions = EagerResolveOptions.SingletonAndScoped)]
public sealed partial class EagerResolveContainer;
