using System.Collections;

namespace SourceGen.Ioc.TestAot.TestCase;

#pragma warning disable SGIOC011 // Duplicated Registration Detected: several containers register the same services

internal sealed class ImporterHandlerX : ICombinedHandler
{
    public string Name => "ImporterX";
}

internal sealed class ImporterHandlerY : ICombinedHandler
{
    public string Name => "ImporterY";
}

internal sealed class ImporterKeyHandlerX : ICombinedHandler
{
    public string Name => "ImporterKeyX";
}

internal sealed class ImporterKeyHandlerK1 : ICombinedHandler
{
    public string Name => "ImporterK1";
}

internal sealed class ImporterKeyHandlerK2 : ICombinedHandler
{
    public string Name => "ImporterK2";
}

/// <summary>Consumer of the collections of <see cref="ICombinedHandler"/>, which combine the module's and the importer's services.</summary>
public sealed class CombinedHandlerConsumer(
    IEnumerable<ICombinedHandler> enumerable,
    IReadOnlyList<ICombinedHandler> readOnlyList,
    ICombinedHandler[] array,
    Func<IEnumerable<ICombinedHandler>> enumerableFactory,
    [IocInject(Key = "k")] IEnumerable<ICombinedHandler> keyedEnumerable,
    [IocInject(Key = "k")] ICombinedHandler[] keyedArray,
    [IocInject("global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey", KeyType = KeyType.Csharp)] IEnumerable<ICombinedHandler> allKeyed,
    IEnumerable<Lazy<ICombinedHandler>> lazyHandlers,
    IEnumerable<Func<ICombinedHandler>> handlerFactories)
{
    public IEnumerable<Lazy<ICombinedHandler>> LazyHandlers { get; } = lazyHandlers;
    public IEnumerable<Func<ICombinedHandler>> HandlerFactories { get; } = handlerFactories;
    public IEnumerable<ICombinedHandler> Enumerable { get; } = enumerable;
    public IReadOnlyList<ICombinedHandler> ReadOnlyList { get; } = readOnlyList;
    public ICombinedHandler[] Array { get; } = array;
    public Func<IEnumerable<ICombinedHandler>> EnumerableFactory { get; } = enumerableFactory;
    public IEnumerable<ICombinedHandler> KeyedEnumerable { get; } = keyedEnumerable;
    public ICombinedHandler[] KeyedArray { get; } = keyedArray;
    public IEnumerable<ICombinedHandler> AllKeyed { get; } = allKeyed;
}

/// <summary>Imports two registrations of each key and registers two non-keyed, one under <c>"x"</c> and two under <c>"k"</c>.</summary>
[IocImportModule<CombinedHandlerModule>]
[IocRegisterFor<ImporterHandlerX>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ImporterHandlerY>(ServiceLifetime.Scoped, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ImporterKeyHandlerX>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)], Key = "x")]
[IocRegisterFor<ImporterKeyHandlerK1>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)], Key = "k")]
[IocRegisterFor<ImporterKeyHandlerK2>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)], Key = "k")]
[IocRegisterFor<CombinedHandlerConsumer>(ServiceLifetime.Scoped)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class CombinedHandlerImporter
{
    public partial IEnumerable<ICombinedHandler> GetHandlers();

    public partial ICombinedHandler[] HandlerArray { get; }
}

/// <summary>Imports two registrations of each key and registers one non-keyed and one under <c>"k"</c>.</summary>
[IocImportModule<CombinedHandlerModule>]
[IocRegisterFor<ImporterHandlerX>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ImporterKeyHandlerK1>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)], Key = "k")]
[IocRegisterFor<CombinedHandlerConsumer>(ServiceLifetime.Scoped)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class CombinedHandlerSingleImporter;

/// <summary>Imports one registration of each key and registers two non-keyed and two under <c>"k"</c>.</summary>
[IocImportModule<SingleCombinedHandlerModule>]
[IocRegisterFor<ImporterHandlerX>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ImporterHandlerY>(ServiceLifetime.Scoped, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ImporterKeyHandlerK1>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)], Key = "k")]
[IocRegisterFor<ImporterKeyHandlerK2>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)], Key = "k")]
[IocRegisterFor<CombinedHandlerConsumer>(ServiceLifetime.Scoped)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class SingleModuleCombinedHandlerImporter;

/// <summary>Imports one registration of each key and registers one non-keyed and one under <c>"k"</c>: no side has a collection resolver.</summary>
[IocImportModule<SingleCombinedHandlerModule>]
[IocRegisterFor<ImporterHandlerX>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ImporterKeyHandlerK1>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)], Key = "k")]
[IocContainer(ExplicitOnly = true)]
public sealed partial class SingleModuleCombinedHandlerSingleImporter
{
    public partial IEnumerable<ICombinedHandler> GetHandlers();
}

/// <summary>Imports <see cref="SingleModuleCombinedHandlerSingleImporter"/> (which combines single registrations) and registers one non-keyed and one under <c>"k"</c>.</summary>
[IocImportModule<SingleModuleCombinedHandlerSingleImporter>]
[IocRegisterFor<ImporterHandlerY>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ImporterKeyHandlerK2>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)], Key = "k")]
[IocContainer(ExplicitOnly = true)]
public sealed partial class NestedSingleCombinedHandlerImporter;

/// <summary>Imports <see cref="CombinedHandlerImporter"/> (which combines collections) and registers one non-keyed service.</summary>
[IocImportModule<CombinedHandlerImporter>]
[IocRegisterFor<ImporterHandlerY>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocContainer(ExplicitOnly = true)]
public sealed partial class NestedCombinedHandlerImporter;

/// <summary>An explicitly registered <c>IEnumerable&lt;ICombinedHandler&gt;</c>.</summary>
public sealed class ExplicitCombinedHandlerList(string name) : IEnumerable<ICombinedHandler>
{
    public static readonly ExplicitCombinedHandlerList Module = new("ExplicitModule");

    public static readonly ExplicitCombinedHandlerList Importer = new("ExplicitImporter");

    public string Name { get; } = name;

    public IEnumerator<ICombinedHandler> GetEnumerator() => Enumerable.Empty<ICombinedHandler>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Module registering two services and their <c>IEnumerable&lt;T&gt;</c> explicitly.</summary>
[IocRegisterFor<ImporterHandlerX>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ImporterHandlerY>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ExplicitCombinedHandlerList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<ICombinedHandler>)], Instance = nameof(ExplicitCombinedHandlerList.Module))]
[IocContainer(ExplicitOnly = true)]
public sealed partial class ExplicitEnumerableCombinedHandlerModule;

/// <summary>Imports a module whose <c>IEnumerable&lt;T&gt;</c> is registered explicitly and registers two services.</summary>
[IocImportModule<ExplicitEnumerableCombinedHandlerModule>]
[IocRegisterFor<ImporterHandlerX>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ImporterHandlerY>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocContainer(ExplicitOnly = true)]
public sealed partial class ExplicitEnumerableModuleImporter;

/// <summary>Imports a module with two registrations and registers <c>IEnumerable&lt;T&gt;</c> explicitly.</summary>
[IocImportModule<CombinedHandlerModule>]
[IocRegisterFor<ImporterHandlerX>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ExplicitCombinedHandlerList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<ICombinedHandler>)], Instance = nameof(ExplicitCombinedHandlerList.Importer))]
[IocContainer(ExplicitOnly = true)]
public sealed partial class ExplicitEnumerableImporter;
