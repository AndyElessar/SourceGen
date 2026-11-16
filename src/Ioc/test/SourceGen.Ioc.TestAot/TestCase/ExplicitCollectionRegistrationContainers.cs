using System.Collections;

namespace SourceGen.Ioc.TestAot.TestCase;

#pragma warning disable SGIOC011 // Duplicated Registration Detected: both containers register the same services

/// <summary>Service registered twice (non-keyed and under <c>"a"</c>), whose collections are also registered explicitly.</summary>
public interface IExplicitCollectionHandler;

internal sealed class FirstExplicitCollectionHandler : IExplicitCollectionHandler;

internal sealed class SecondExplicitCollectionHandler : IExplicitCollectionHandler;

/// <summary>An explicitly registered collection of <see cref="IExplicitCollectionHandler"/>.</summary>
public sealed class ExplicitHandlerList : IReadOnlyList<IExplicitCollectionHandler>
{
    public static readonly ExplicitHandlerList Default = new();

    public static readonly ExplicitHandlerList Keyed = new();

    public static readonly ExplicitHandlerList AllKeys = new();

    public IExplicitCollectionHandler this[int index] => throw new ArgumentOutOfRangeException(nameof(index));

    public int Count => 0;

    public IEnumerator<IExplicitCollectionHandler> GetEnumerator() => Enumerable.Empty<IExplicitCollectionHandler>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Consumer of the collections that are registered explicitly.</summary>
public sealed class ExplicitCollectionConsumer(
    IEnumerable<IExplicitCollectionHandler> enumerable,
    IReadOnlyList<IExplicitCollectionHandler> readOnlyList,
    IExplicitCollectionHandler[] array,
    [IocInject(Key = "a")] IEnumerable<IExplicitCollectionHandler> keyedEnumerable,
    [IocInject(Key = "a")] IExplicitCollectionHandler[] keyedArray)
{
    public IEnumerable<IExplicitCollectionHandler> Enumerable { get; } = enumerable;
    public IReadOnlyList<IExplicitCollectionHandler> ReadOnlyList { get; } = readOnlyList;
    public IExplicitCollectionHandler[] Array { get; } = array;
    public IEnumerable<IExplicitCollectionHandler> KeyedEnumerable { get; } = keyedEnumerable;
    public IExplicitCollectionHandler[] KeyedArray { get; } = keyedArray;
}

[IocRegisterFor<FirstExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)])]
[IocRegisterFor<SecondExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)])]
[IocRegisterFor<FirstExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)], Key = "a")]
[IocRegisterFor<SecondExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)], Key = "a")]
[IocRegisterFor<ExplicitHandlerList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<IExplicitCollectionHandler>), typeof(IReadOnlyList<IExplicitCollectionHandler>)], Instance = nameof(ExplicitHandlerList.Default))]
[IocRegisterFor<ExplicitHandlerList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<IExplicitCollectionHandler>), typeof(IReadOnlyList<IExplicitCollectionHandler>)], Instance = nameof(ExplicitHandlerList.Keyed), Key = "a")]
[IocRegisterFor<ExplicitHandlerList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<IExplicitCollectionHandler>)], Instance = nameof(ExplicitHandlerList.AllKeys), Key = "KeyedService.AnyKey", KeyType = KeyType.Csharp)]
[IocRegisterFor<ExplicitCollectionConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class ExplicitCollectionContainer;

[IocRegisterFor<FirstExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)])]
[IocRegisterFor<SecondExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)])]
[IocRegisterFor<FirstExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)], Key = "a")]
[IocRegisterFor<SecondExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)], Key = "a")]
[IocRegisterFor<ExplicitHandlerList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<IExplicitCollectionHandler>), typeof(IReadOnlyList<IExplicitCollectionHandler>)], Instance = nameof(ExplicitHandlerList.Default))]
[IocRegisterFor<ExplicitHandlerList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<IExplicitCollectionHandler>), typeof(IReadOnlyList<IExplicitCollectionHandler>)], Instance = nameof(ExplicitHandlerList.Keyed), Key = "a")]
[IocRegisterFor<ExplicitHandlerList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<IExplicitCollectionHandler>)], Instance = nameof(ExplicitHandlerList.AllKeys), Key = "KeyedService.AnyKey", KeyType = KeyType.Csharp)]
[IocRegisterFor<ExplicitCollectionConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true, UseSwitchStatement = true)]
public sealed partial class ExplicitCollectionSwitchContainer;

/// <summary>Imports <see cref="ExplicitCollectionContainer"/>, whose explicit collections win through the importer too.</summary>
[IocImportModule<ExplicitCollectionContainer>]
[IocContainer(ExplicitOnly = true)]
public sealed partial class ExplicitCollectionImporter;
