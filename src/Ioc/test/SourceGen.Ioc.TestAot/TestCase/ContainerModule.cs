namespace SourceGen.Ioc.TestAot.TestCase;

/// <summary>
/// Standalone container that implements IIocContainer for container behavior testing.
/// </summary>
/// <remarks>
/// Open generic registrations from external assemblies (like TestCaseModule's OpenGenericModule)
/// are automatically inherited because SOURCEGEN constant is defined in tests/Directory.Build.props.
/// This ensures [Conditional("SOURCEGEN")] attributes are emitted to IL metadata.
/// </remarks>
[IocImportModule<TestCaseModule>]
[IocContainer]
public sealed partial class ContainerModule;
