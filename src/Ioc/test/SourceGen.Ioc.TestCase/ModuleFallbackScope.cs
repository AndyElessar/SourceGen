namespace SourceGen.Ioc.TestCase;

/// <summary>Service registered only in the fallback <c>IServiceCollection</c> of the importing container.</summary>
public sealed class ModuleFallbackExternalService : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}

/// <summary>Scoped service of <see cref="InnerFallbackScopeModule"/> that depends on a fallback-provider service.</summary>
public sealed class InnerModuleFallbackConsumer(ModuleFallbackExternalService external)
{
    public ModuleFallbackExternalService External { get; } = external;
}

/// <summary>Scoped service of <see cref="FallbackScopeModule"/> that depends on a fallback-provider service.</summary>
public sealed class ModuleFallbackConsumer(ModuleFallbackExternalService external)
{
    public ModuleFallbackExternalService External { get; } = external;
}

/// <summary>Module imported by <see cref="FallbackScopeModule"/>, to test nested module scopes.</summary>
[IocRegisterFor<InnerModuleFallbackConsumer>(ServiceLifetime.Scoped)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class InnerFallbackScopeModule;

/// <summary>Module whose scoped services resolve fallback-provider services of the importing container.</summary>
[IocImportModule<InnerFallbackScopeModule>]
[IocRegisterFor<ModuleFallbackConsumer>(ServiceLifetime.Scoped)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class FallbackScopeModule;
