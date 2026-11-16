namespace SourceGen.Ioc.TestCase;

#pragma warning disable SGIOC011 // Duplicated Registration Detected: both containers register the same services

// Several registrations of one service type under one key. Keyed collections are never requested in this compilation,
// except by KeyedMultiCollectionConsumer, so the keyed collection resolvers must not depend on demand detection.

/// <summary>Service registered several times under the same keys.</summary>
public interface IKeyedMultiService
{
    string Name { get; }
}

/// <summary>Keys of <see cref="IKeyedMultiService"/> registrations.</summary>
public enum KeyedMultiKind
{
    First,
    Second,
}

/// <summary>Keys written as C# expressions (<see cref="KeyType.Csharp"/>).</summary>
public static class KeyedMultiKeys
{
    public const string Csharp = "csharp-key";
}

internal sealed class AlphaFirstKeyedMultiService : IKeyedMultiService
{
    public string Name => "Alpha1";
}

internal sealed class BetaKeyedMultiService : IKeyedMultiService
{
    public string Name => "Beta";
}

internal sealed class AlphaSecondKeyedMultiService : IKeyedMultiService
{
    public string Name => "Alpha2";
}

internal sealed class DefaultKeyedMultiService : IKeyedMultiService
{
    public string Name => "Default";
}

internal sealed class FirstEnumKeyedMultiService : IKeyedMultiService
{
    public string Name => "Enum1";
}

internal sealed class SecondEnumKeyedMultiService : IKeyedMultiService
{
    public string Name => "Enum2";
}

internal sealed class FirstCsharpKeyedMultiService : IKeyedMultiService
{
    public string Name => "Csharp1";
}

internal sealed class SecondCsharpKeyedMultiService : IKeyedMultiService
{
    public string Name => "Csharp2";
}

/// <summary>Consumer of the keyed collections of <see cref="IKeyedMultiService"/> under <c>"alpha"</c>.</summary>
public sealed class KeyedMultiCollectionConsumer(
    [IocInject(Key = "alpha")] IEnumerable<IKeyedMultiService> enumerable,
    [IocInject(Key = "alpha")] IReadOnlyList<IKeyedMultiService> readOnlyList,
    [IocInject(Key = "alpha")] IKeyedMultiService[] array,
    [IocInject(Key = "beta")] IEnumerable<IKeyedMultiService> single)
{
    public IEnumerable<IKeyedMultiService> Enumerable { get; } = enumerable;
    public IReadOnlyList<IKeyedMultiService> ReadOnlyList { get; } = readOnlyList;
    public IKeyedMultiService[] Array { get; } = array;
    public IEnumerable<IKeyedMultiService> Single { get; } = single;
}

[IocRegisterFor<AlphaFirstKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)], Key = "alpha")]
[IocRegisterFor<BetaKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)], Key = "beta")]
[IocRegisterFor<AlphaSecondKeyedMultiService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IKeyedMultiService)], Key = "alpha")]
[IocRegisterFor<DefaultKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)])]
[IocRegisterFor<FirstEnumKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)], Key = KeyedMultiKind.First, KeyType = KeyType.Value)]
[IocRegisterFor<SecondEnumKeyedMultiService>(ServiceLifetime.Transient, ServiceTypes = [typeof(IKeyedMultiService)], Key = KeyedMultiKind.First, KeyType = KeyType.Value)]
[IocRegisterFor<FirstCsharpKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)], Key = "global::SourceGen.Ioc.TestCase.KeyedMultiKeys.Csharp", KeyType = KeyType.Csharp)]
[IocRegisterFor<SecondCsharpKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)], Key = "global::SourceGen.Ioc.TestCase.KeyedMultiKeys.Csharp", KeyType = KeyType.Csharp)]
[IocRegisterFor<KeyedMultiCollectionConsumer>(ServiceLifetime.Scoped)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class KeyedMultiCollectionModule;

[IocRegisterFor<AlphaFirstKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)], Key = "alpha")]
[IocRegisterFor<BetaKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)], Key = "beta")]
[IocRegisterFor<AlphaSecondKeyedMultiService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IKeyedMultiService)], Key = "alpha")]
[IocRegisterFor<DefaultKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)])]
[IocRegisterFor<FirstEnumKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)], Key = KeyedMultiKind.First, KeyType = KeyType.Value)]
[IocRegisterFor<SecondEnumKeyedMultiService>(ServiceLifetime.Transient, ServiceTypes = [typeof(IKeyedMultiService)], Key = KeyedMultiKind.First, KeyType = KeyType.Value)]
[IocRegisterFor<FirstCsharpKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)], Key = "global::SourceGen.Ioc.TestCase.KeyedMultiKeys.Csharp", KeyType = KeyType.Csharp)]
[IocRegisterFor<SecondCsharpKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)], Key = "global::SourceGen.Ioc.TestCase.KeyedMultiKeys.Csharp", KeyType = KeyType.Csharp)]
[IocRegisterFor<KeyedMultiCollectionConsumer>(ServiceLifetime.Scoped)]
[IocContainer(ExplicitOnly = true, UseSwitchStatement = true)]
public sealed partial class KeyedMultiCollectionSwitchModule;

/// <summary>
/// Module without <c>IntegrateServiceProvider</c> with two registrations under one key, imported (from another assembly)
/// by a container that integrates the fallback provider.
/// </summary>
[IocRegisterFor<AlphaFirstKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)], Key = "alpha")]
[IocRegisterFor<BetaKeyedMultiService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedMultiService)], Key = "beta")]
[IocRegisterFor<AlphaSecondKeyedMultiService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IKeyedMultiService)], Key = "alpha")]
[IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
public sealed partial class NonIntegratedKeyedMultiModule;
