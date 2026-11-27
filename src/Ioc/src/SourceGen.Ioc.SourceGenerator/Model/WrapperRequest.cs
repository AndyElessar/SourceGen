namespace SourceGen.Ioc.Model;

/// <summary>The outermost shape of a requested type (Analysis.Model.spec, "Grammar of wrapper types").</summary>
internal enum RequestShape
{
    /// <summary><c>Element</c>: one service, possibly in a <c>KeyValuePair</c> or a deferred wrapper.</summary>
    Single,

    /// <summary><c>Collection&lt;Element&gt;</c>.</summary>
    Collection,

    /// <summary><c>Dictionary&lt;TKey, Deferred&gt;</c>.</summary>
    Dictionary,
}

/// <summary>The deferred wrapper of an element.</summary>
internal enum DeferredKind
{
    /// <summary>No deferred wrapper.</summary>
    None,

    /// <summary><c>Lazy&lt;Async&gt;</c>.</summary>
    Lazy,

    /// <summary><c>Func&lt;Async&gt;</c> or <c>Func&lt;A1, ..., An, Async&gt;</c>.</summary>
    Func,
}

/// <summary>A requested type, taken apart by the grammar of wrapper types (Analysis.Model.spec, "Grammar of wrapper types").</summary>
/// <param name="Shape">The outermost shape.</param>
/// <param name="KeyValuePair">Whether the element is <c>KeyValuePair&lt;TKey, Deferred&gt;</c>.</param>
/// <param name="KeyType">The <c>TKey</c> of the dictionary or of the <c>KeyValuePair</c>.</param>
/// <param name="Deferred">The deferred wrapper.</param>
/// <param name="FuncArguments">The arguments <c>A1..An</c> of <c>Func&lt;A1, ..., An, Async&gt;</c>. Empty for the other shapes.</param>
/// <param name="IsTask">Whether the service is asked for as <c>Task&lt;T&gt;</c>.</param>
/// <param name="Core">The core service type <c>T</c>. It is never a wrapper type.</param>
internal sealed record WrapperRequest(
    RequestShape Shape,
    bool KeyValuePair,
    TypeRef? KeyType,
    DeferredKind Deferred,
    ImmutableEquatableArray<TypeRef> FuncArguments,
    bool IsTask,
    TypeRef Core)
{
    private const string Lazy = "System.Lazy`1";
    private const string Task = "System.Threading.Tasks.Task`1";
    private const string Pair = "System.Collections.Generic.KeyValuePair`2";

    private static readonly HashSet<string> Collections =
    [
        "System.Collections.Generic.IEnumerable`1",
        "System.Collections.Generic.IReadOnlyCollection`1",
        "System.Collections.Generic.ICollection`1",
        "System.Collections.Generic.IReadOnlyList`1",
        "System.Collections.Generic.IList`1",
    ];

    private static readonly HashSet<string> Dictionaries =
    [
        "System.Collections.Generic.IDictionary`2",
        "System.Collections.Generic.IReadOnlyDictionary`2",
        "System.Collections.Generic.Dictionary`2",
    ];

    /// <summary>The metadata name of <c>IEnumerable&lt;T&gt;</c>.</summary>
    public const string Enumerable = "System.Collections.Generic.IEnumerable`1";

    /// <summary>Gets a value indicating whether the request is the service itself, without a wrapper.</summary>
    public bool IsPlain => Shape == RequestShape.Single && !KeyValuePair && Deferred == DeferredKind.None && !IsTask;

    /// <summary>Gets a value indicating whether the request is a collection whose element is the service itself.</summary>
    public bool IsPlainCollection => Shape == RequestShape.Collection && !KeyValuePair && Deferred == DeferredKind.None && !IsTask;

    /// <summary>
    /// Gets a value indicating whether the element is the service itself, or the service in <c>Lazy</c>, <c>Func</c>
    /// without arguments, <c>Task</c>, or a nested combination of them. <c>KeyValuePair</c> and <c>Func&lt;A..., T&gt;</c>
    /// come in later slices.
    /// </summary>
    public bool HasSimpleElement => !KeyValuePair && FuncArguments.Length == 0;

    /// <summary>Gets a value indicating whether the request is one service in <c>Lazy</c>, <c>Func</c> or <c>Task</c> (see <see cref="HasSimpleElement"/>).</summary>
    public bool IsSingleWrapper => Shape == RequestShape.Single && HasSimpleElement && !IsPlain;

    /// <summary>Gets a value indicating whether the request is a collection whose element is the service itself or a wrapper of it (see <see cref="HasSimpleElement"/>).</summary>
    public bool IsSimpleCollection => Shape == RequestShape.Collection && HasSimpleElement;

    /// <summary>Gets a value indicating whether the element is the service in <c>Lazy</c>, <c>Func</c> or <c>Task</c>.</summary>
    public bool IsWrappedElement => Deferred != DeferredKind.None || IsTask;

    /// <summary>
    /// Gets a value indicating whether the request is one <c>KeyValuePair</c> whose value is the service, or the service
    /// in <c>Lazy</c>, <c>Func</c> without arguments, or <c>Task</c>.
    /// </summary>
    public bool IsSinglePair => Shape == RequestShape.Single && KeyValuePair && FuncArguments.Length == 0;

    /// <summary>
    /// Gets a value indicating whether the request is one <c>Func&lt;A1, ..., An, T&gt;</c> or
    /// <c>Func&lt;A1, ..., An, Task&lt;T&gt;&gt;</c> (<c>n ≥ 1</c>). In a collection, a pair or a dictionary, it comes later.
    /// </summary>
    public bool IsSingleFuncWithArguments => Shape == RequestShape.Single && !KeyValuePair && FuncArguments.Length > 0;

    /// <summary>
    /// The reason that <c>SGIOC305</c> reports when two arguments have the same type (Analysis.Model.spec, "Mapping of
    /// <c>Func&lt;..., T&gt;</c>", condition 4), or <see langword="null"/>. It does not depend on the candidates.
    /// </summary>
    public string? DuplicateArgument()
        => FuncArguments.GroupBy(static a => a).FirstOrDefault(static g => g.Count() > 1) is { } duplicate
            ? $"the argument type '{Emit.TypeNames.Display(duplicate.Key)}' occurs more than once"
            : null;

    /// <summary>
    /// The constructor parameter of <paramref name="target"/> that each argument maps to (Analysis.Model.spec, "Mapping
    /// of <c>Func&lt;..., T&gt;</c>", conditions 2, 3 and 5), or the reason that <c>SGIOC305</c> reports. A parameter with
    /// <c>[ServiceKey]</c> takes no part.
    /// </summary>
    /// <param name="target">The closed definition that <c>T</c> resolves to.</param>
    /// <param name="mapping">The index of the constructor parameter of each argument.</param>
    /// <returns>The reason, or <see langword="null"/> when the mapping is valid.</returns>
    public string? Map(ClosedDefinition target, out ImmutableEquatableArray<int> mapping)
    {
        mapping = [];
        if(target.Source.Kind != SourceKind.Implementation)
        {
            // A factory or an instance has no constructor to map the arguments to
            return $"'{Emit.KeyNames.Definition(target)}' is not a registration of an implementation type";
        }

        if(target.Lifetime != Lifetime.Transient)
        {
            return $"'{Emit.KeyNames.Definition(target)}' is {target.Lifetime}; it must be Transient";
        }

        int[] result = new int[FuncArguments.Length];
        for(int a = 0; a < FuncArguments.Length; a++)
        {
            int[] matches = [.. System.Linq.Enumerable.Range(0, target.ConstructorParameters.Length)
                .Where(p => target.ConstructorParameters[p].KeyMode != SiteKeyMode.ServiceKey && target.ConstructorParameters[p].Type.Equals(FuncArguments[a]))];
            if(matches.Length != 1)
            {
                return $"the argument type '{Emit.TypeNames.Display(FuncArguments[a])}' matches {(matches.Length == 0 ? "no" : "more than one")} constructor parameter of '{Emit.KeyNames.Definition(target)}'";
            }

            result[a] = matches[0];
        }

        mapping = result.ToImmutableEquatableArray();
        return null;
    }

    /// <summary>
    /// Gets a value indicating whether the request is a collection of <c>KeyValuePair</c> or a dictionary whose value is
    /// the service, or the service in <c>Lazy</c>, <c>Func</c> without arguments, or <c>Task</c>.
    /// </summary>
    public bool IsKeyedCollection => FuncArguments.Length == 0 && (Shape == RequestShape.Dictionary || (Shape == RequestShape.Collection && KeyValuePair));

    /// <summary>Gets the request of one value of a <c>KeyValuePair</c> or of a dictionary: one service, without the key.</summary>
    public WrapperRequest PairValue => this with { Shape = RequestShape.Single, KeyValuePair = false, KeyType = null };

    /// <summary>
    /// Whether a registered key matches <c>TKey</c>: the key type of the key is <c>TKey</c>, or <c>TKey</c> is
    /// <c>object</c> (Analysis.Model.spec, "Grammar of wrapper types").
    /// </summary>
    /// <param name="key">The key of the registration.</param>
    /// <param name="keyType">The <c>TKey</c>.</param>
    /// <returns>Whether the registration matches.</returns>
    public static bool MatchesKeyType(KeyRef key, TypeRef keyType)
        => keyType is NamedTypeRef { MetadataName: "System.Object" } || key.ValueType.Equals(keyType);

    /// <summary>Takes <paramref name="type"/> apart by the grammar.</summary>
    /// <param name="type">The requested type.</param>
    /// <returns>The request, or <see langword="null"/> when the combination of wrapper types is invalid (<c>SGIOC304</c>).</returns>
    public static WrapperRequest? Parse(TypeRef type)
    {
        if(ElementOf(type) is { } element)
        {
            return ParseElement(element, RequestShape.Collection);
        }

        if(type is NamedTypeRef { Arguments: [TypeRef key, TypeRef value] } dictionary && Dictionaries.Contains(dictionary.MetadataName))
        {
            return ParseDeferred(value, RequestShape.Dictionary, false, key);
        }

        return ParseElement(type, RequestShape.Single);
    }

    /// <summary>The element of a collection of the grammar, or <see langword="null"/>.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The element type.</returns>
    public static TypeRef? ElementOf(TypeRef type) => type switch
    {
        ArrayTypeRef { Rank: 1 } array => array.Element,
        NamedTypeRef { Arguments: [TypeRef element] } named when Collections.Contains(named.MetadataName) => element,
        _ => null,
    };

    private static WrapperRequest? ParseElement(TypeRef type, RequestShape shape)
        => type is NamedTypeRef { MetadataName: Pair, Arguments: [TypeRef key, TypeRef value] }
            ? ParseDeferred(value, shape, true, key)
            : ParseDeferred(type, shape, false, null);

    private static WrapperRequest? ParseDeferred(TypeRef type, RequestShape shape, bool pair, TypeRef? keyType)
    {
        if(type is NamedTypeRef { MetadataName: Lazy, Arguments: [TypeRef lazy] })
        {
            return ParseAsync(lazy, shape, pair, keyType, DeferredKind.Lazy, []);
        }

        if(type is NamedTypeRef { MetadataName: var name, Arguments: { } arguments } && name.StartsWith("System.Func`", StringComparison.Ordinal))
        {
            return ParseAsync(arguments[^1], shape, pair, keyType, DeferredKind.Func, [.. arguments.Take(arguments.Length - 1)]);
        }

        return ParseAsync(type, shape, pair, keyType, DeferredKind.None, []);
    }

    private static WrapperRequest? ParseAsync(TypeRef type, RequestShape shape, bool pair, TypeRef? keyType, DeferredKind deferred, ImmutableEquatableArray<TypeRef> arguments)
    {
        bool isTask = type is NamedTypeRef { MetadataName: Task, Arguments: [_] };
        TypeRef core = isTask ? ((NamedTypeRef)type).Arguments![0] : type;
        return WrapperTypes.IsWrapper(core) ? null : new WrapperRequest(shape, pair, keyType, deferred, arguments, isTask, core);
    }
}
