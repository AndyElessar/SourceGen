namespace SourceGen.Ioc.TestAot.TestCase;

/// <summary>Consumer of a keyed collection of an imported module's service.</summary>
public sealed class ImportedKeyedMultiCollectionConsumer([IocInject(Key = "alpha")] IEnumerable<IKeyedMultiService> services)
{
    public IEnumerable<IKeyedMultiService> Services { get; } = services;
}

/// <summary>
/// Container importing a module without fallback integration (from another assembly) that registers two services under
/// one key: its keyed collections merge the fallback services once, through this container.
/// </summary>
[IocImportModule<NonIntegratedKeyedMultiModule>]
[IocRegisterFor<ImportedKeyedMultiCollectionConsumer>(ServiceLifetime.Scoped)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class KeyedMultiModuleImporter;
