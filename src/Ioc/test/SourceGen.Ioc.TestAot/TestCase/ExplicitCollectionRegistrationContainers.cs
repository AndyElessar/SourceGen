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

/// <summary>Explicitly registered collections of <c>Lazy&lt;T&gt;</c>, <c>Func&lt;T&gt;</c> and <c>KeyValuePair&lt;K, V&gt;</c> elements.</summary>
public static class ExplicitWrapperCollections
{
    public static readonly LazyList Lazies = new();

    public static readonly FuncList Funcs = new();

    public static readonly PairList Pairs = new();

    public static readonly HandlerDictionary Dictionary = new();

    public sealed class LazyList : List<Lazy<IExplicitCollectionHandler>>;

    public sealed class FuncList : List<Func<IExplicitCollectionHandler>>;

    public sealed class PairList : List<KeyValuePair<string, IExplicitCollectionHandler>>;

    public sealed class HandlerDictionary : Dictionary<string, IExplicitCollectionHandler>;
}

/// <summary>Consumer of the wrapper collections, which makes the container generate their collection resolvers.</summary>
public sealed class ExplicitWrapperCollectionConsumer(
    IEnumerable<Lazy<IExplicitCollectionHandler>> lazies,
    IEnumerable<Func<IExplicitCollectionHandler>> funcs,
    IEnumerable<KeyValuePair<string, IExplicitCollectionHandler>> pairs,
    IReadOnlyDictionary<string, IExplicitCollectionHandler> dictionary,
    Lazy<IExplicitCollectionHandler>[] lazyArray,
    Lazy<IReadOnlyDictionary<string, IExplicitCollectionHandler>> lazyDictionary,
    Func<IReadOnlyDictionary<string, IExplicitCollectionHandler>> dictionaryFactory)
{
    public Lazy<IReadOnlyDictionary<string, IExplicitCollectionHandler>> LazyDictionary { get; } = lazyDictionary;
    public Func<IReadOnlyDictionary<string, IExplicitCollectionHandler>> DictionaryFactory { get; } = dictionaryFactory;
    public IEnumerable<Lazy<IExplicitCollectionHandler>> Lazies { get; } = lazies;
    public IEnumerable<Func<IExplicitCollectionHandler>> Funcs { get; } = funcs;
    public IEnumerable<KeyValuePair<string, IExplicitCollectionHandler>> Pairs { get; } = pairs;
    public IReadOnlyDictionary<string, IExplicitCollectionHandler> Dictionary { get; } = dictionary;
    public Lazy<IExplicitCollectionHandler>[] LazyArray { get; } = lazyArray;
}

[IocRegisterFor<FirstExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)])]
[IocRegisterFor<SecondExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)])]
[IocRegisterFor<FirstExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)], Key = "a")]
[IocRegisterFor<SecondExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)], Key = "b")]
[IocRegisterFor<ExplicitWrapperCollections.LazyList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<Lazy<IExplicitCollectionHandler>>)], Instance = "ExplicitWrapperCollections.Lazies")]
[IocRegisterFor<ExplicitWrapperCollections.FuncList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<Func<IExplicitCollectionHandler>>)], Instance = "ExplicitWrapperCollections.Funcs")]
[IocRegisterFor<ExplicitWrapperCollections.PairList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<KeyValuePair<string, IExplicitCollectionHandler>>)], Instance = "ExplicitWrapperCollections.Pairs")]
[IocRegisterFor<ExplicitWrapperCollections.HandlerDictionary>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IReadOnlyDictionary<string, IExplicitCollectionHandler>)], Instance = "ExplicitWrapperCollections.Dictionary")]
[IocRegisterFor<ExplicitWrapperCollectionConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class ExplicitWrapperCollectionContainer;

[IocRegisterFor<FirstExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)])]
[IocRegisterFor<SecondExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)])]
[IocRegisterFor<FirstExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)], Key = "a")]
[IocRegisterFor<SecondExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)], Key = "b")]
[IocRegisterFor<ExplicitWrapperCollections.LazyList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<Lazy<IExplicitCollectionHandler>>)], Instance = "ExplicitWrapperCollections.Lazies")]
[IocRegisterFor<ExplicitWrapperCollections.FuncList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<Func<IExplicitCollectionHandler>>)], Instance = "ExplicitWrapperCollections.Funcs")]
[IocRegisterFor<ExplicitWrapperCollections.PairList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<KeyValuePair<string, IExplicitCollectionHandler>>)], Instance = "ExplicitWrapperCollections.Pairs")]
[IocRegisterFor<ExplicitWrapperCollections.HandlerDictionary>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IReadOnlyDictionary<string, IExplicitCollectionHandler>)], Instance = "ExplicitWrapperCollections.Dictionary")]
[IocRegisterFor<ExplicitWrapperCollectionConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true, UseSwitchStatement = true)]
public sealed partial class ExplicitWrapperCollectionSwitchContainer;

/// <summary>Explicitly registered single <c>Lazy&lt;T&gt;</c> and <c>Func&lt;T&gt;</c> of <see cref="IExplicitCollectionHandler"/>.</summary>
public static class ExplicitWrappers
{
    public static readonly Lazy<IExplicitCollectionHandler> Lazy = new(static () => new FirstExplicitCollectionHandler());

    public static readonly Func<IExplicitCollectionHandler> Func = static () => new FirstExplicitCollectionHandler();
}

/// <summary>
/// Consumer of the explicitly registered wrappers, directly, inside another wrapper, and as collection elements: like
/// <c>MS.DI</c>, a collection of a wrapper type registered explicitly contains the explicit registrations only.
/// </summary>
public sealed class ExplicitWrapperConsumer(
    Lazy<IExplicitCollectionHandler> lazy,
    Func<IExplicitCollectionHandler> func,
    Lazy<Func<IExplicitCollectionHandler>> lazyFunc,
    IEnumerable<Lazy<IExplicitCollectionHandler>> lazies,
    Lazy<IExplicitCollectionHandler>[] lazyArray,
    IEnumerable<Func<IExplicitCollectionHandler>> funcs)
{
    public Lazy<IExplicitCollectionHandler> Lazy { get; } = lazy;
    public Func<IExplicitCollectionHandler> Func { get; } = func;
    public Lazy<Func<IExplicitCollectionHandler>> LazyFunc { get; } = lazyFunc;
    public IEnumerable<Lazy<IExplicitCollectionHandler>> Lazies { get; } = lazies;
    public Lazy<IExplicitCollectionHandler>[] LazyArray { get; } = lazyArray;
    public IEnumerable<Func<IExplicitCollectionHandler>> Funcs { get; } = funcs;
}

[IocRegisterFor<SecondExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)])]
[IocRegisterFor<Lazy<IExplicitCollectionHandler>>(ServiceLifetime.Singleton, Instance = "ExplicitWrappers.Lazy")]
[IocRegisterFor<Func<IExplicitCollectionHandler>>(ServiceLifetime.Singleton, Instance = "ExplicitWrappers.Func")]
[IocRegisterFor<ExplicitWrapperConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class ExplicitWrapperContainer;

[IocRegisterFor<SecondExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)])]
[IocRegisterFor<Lazy<IExplicitCollectionHandler>>(ServiceLifetime.Singleton, Instance = "ExplicitWrappers.Lazy")]
[IocRegisterFor<Func<IExplicitCollectionHandler>>(ServiceLifetime.Singleton, Instance = "ExplicitWrappers.Func")]
[IocRegisterFor<ExplicitWrapperConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true, UseSwitchStatement = true)]
public sealed partial class ExplicitWrapperSwitchContainer;

/// <summary>Consumer of the explicitly registered wrappers only, so no collection of them is requested at compile time.</summary>
public sealed class ExplicitSingleWrapperConsumer(Lazy<IExplicitCollectionHandler> lazy, Func<IExplicitCollectionHandler> func)
{
    public Lazy<IExplicitCollectionHandler> Lazy { get; } = lazy;
    public Func<IExplicitCollectionHandler> Func { get; } = func;
}

[IocRegisterFor<SecondExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)])]
[IocRegisterFor<Lazy<IExplicitCollectionHandler>>(ServiceLifetime.Singleton, Instance = "ExplicitWrappers.Lazy")]
[IocRegisterFor<Func<IExplicitCollectionHandler>>(ServiceLifetime.Singleton, Instance = "ExplicitWrappers.Func")]
[IocRegisterFor<ExplicitSingleWrapperConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class ExplicitSingleWrapperContainer;

[IocRegisterFor<SecondExplicitCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitCollectionHandler)])]
[IocRegisterFor<Lazy<IExplicitCollectionHandler>>(ServiceLifetime.Singleton, Instance = "ExplicitWrappers.Lazy")]
[IocRegisterFor<Func<IExplicitCollectionHandler>>(ServiceLifetime.Singleton, Instance = "ExplicitWrappers.Func")]
[IocRegisterFor<ExplicitSingleWrapperConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true, UseSwitchStatement = true)]
public sealed partial class ExplicitSingleWrapperSwitchContainer;

/// <summary>
/// Service whose BCL collections are registered directly with <c>Instance</c>. It is not
/// <see cref="IExplicitCollectionHandler"/>, whose collection service types other test cases register, so the constructor
/// parameters of the collections resolve to these registrations.
/// </summary>
public interface IExplicitBclCollectionHandler;

internal sealed class FirstExplicitBclCollectionHandler : IExplicitBclCollectionHandler;

internal sealed class SecondExplicitBclCollectionHandler : IExplicitBclCollectionHandler;

/// <summary>
/// BCL collections registered directly with <c>Instance</c>: their <c>List(IEnumerable&lt;T&gt;)</c> /
/// <c>Dictionary(IDictionary&lt;TKey, TValue&gt;, IEqualityComparer&lt;TKey&gt;)</c> constructors resolve to the registrations
/// themselves, which is not a circular dependency (SGIOC002), since an Instance registration is never constructed.
/// </summary>
public static class ExplicitBclCollections
{
    public static readonly List<Lazy<IExplicitBclCollectionHandler>> Lazies = [];

    public static readonly Dictionary<string, IExplicitBclCollectionHandler> Dictionary = [];
}

/// <summary>Consumer of the BCL collections registered with <c>Instance</c>.</summary>
public sealed class ExplicitBclCollectionConsumer(
    IEnumerable<Lazy<IExplicitBclCollectionHandler>> lazies,
    IReadOnlyDictionary<string, IExplicitBclCollectionHandler> dictionary)
{
    public IEnumerable<Lazy<IExplicitBclCollectionHandler>> Lazies { get; } = lazies;
    public IReadOnlyDictionary<string, IExplicitBclCollectionHandler> Dictionary { get; } = dictionary;
}

[IocRegisterFor<FirstExplicitBclCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitBclCollectionHandler)])]
[IocRegisterFor<SecondExplicitBclCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitBclCollectionHandler)])]
[IocRegisterFor<List<Lazy<IExplicitBclCollectionHandler>>>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<Lazy<IExplicitBclCollectionHandler>>)], Instance = "ExplicitBclCollections.Lazies")]
[IocRegisterFor<Dictionary<string, IExplicitBclCollectionHandler>>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IReadOnlyDictionary<string, IExplicitBclCollectionHandler>)], Instance = "ExplicitBclCollections.Dictionary")]
[IocRegisterFor<ExplicitBclCollectionConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class ExplicitBclCollectionContainer;

[IocRegisterFor<FirstExplicitBclCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitBclCollectionHandler)])]
[IocRegisterFor<SecondExplicitBclCollectionHandler>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitBclCollectionHandler)])]
[IocRegisterFor<List<Lazy<IExplicitBclCollectionHandler>>>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<Lazy<IExplicitBclCollectionHandler>>)], Instance = "ExplicitBclCollections.Lazies")]
[IocRegisterFor<Dictionary<string, IExplicitBclCollectionHandler>>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IReadOnlyDictionary<string, IExplicitBclCollectionHandler>)], Instance = "ExplicitBclCollections.Dictionary")]
[IocRegisterFor<ExplicitBclCollectionConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true, UseSwitchStatement = true)]
public sealed partial class ExplicitBclCollectionSwitchContainer;
