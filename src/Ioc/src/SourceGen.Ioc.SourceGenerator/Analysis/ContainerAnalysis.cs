using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Emit;
using SourceGen.Ioc.Model;
using SourceGen.Ioc.Parse;

namespace SourceGen.Ioc.Analysis;

/// <summary>How the container gets the argument of a dependency.</summary>
internal enum ArgumentKind
{
    /// <summary>The resolver of a closed definition.</summary>
    Resolved,

    /// <summary>The container instance that resolves it (root or scope): <c>IServiceProvider</c> and the container class.</summary>
    Self,

    /// <summary>The root: <c>IServiceScopeFactory</c>, <c>IServiceProviderIsService</c>, <c>IServiceProviderIsKeyedService</c>.</summary>
    Root,

    /// <summary>A missing optional dependency: the default value, or <see langword="null"/>.</summary>
    Default,

    /// <summary><c>[ServiceKey]</c>: the key of the closed definition that owns the parameter.</summary>
    ServiceKey,

    /// <summary>
    /// A missing dependency that the fallback provider resolves (of the root, or the fallback scope of a scope). An
    /// optional dependency uses its default value when the fallback provider returns <see langword="null"/>.
    /// </summary>
    Fallback,

    /// <summary>A collection of the service itself: the resolver of the collection <see cref="ArgumentPlan.Target"/>.</summary>
    Collection,

    /// <summary><c>Lazy&lt;T&gt;</c>, <c>Func&lt;T&gt;</c> or <c>Task&lt;T&gt;</c>: the resolver of the wrapper <see cref="ArgumentPlan.Target"/>.</summary>
    Wrapper,

    /// <summary><c>KeyValuePair&lt;TKey, T&gt;</c>: the resolver of the pair <see cref="ArgumentPlan.Target"/>.</summary>
    Pair,

    /// <summary>A collection of <c>KeyValuePair</c> or a dictionary: the resolver of <see cref="ArgumentPlan.Target"/> in <see cref="ContainerPlan.KeyedCollections"/>.</summary>
    Keyed,

    /// <summary>
    /// A required constructor parameter that a <c>Func&lt;A..., T&gt;</c> maps and that the container has no candidate
    /// for: only the caller of the <c>Func</c> supplies it. The resolver of <c>T</c> itself throws.
    /// </summary>
    Unavailable,
}

/// <summary>The resolution result of one dependency site.</summary>
/// <param name="Kind">How the argument is resolved.</param>
/// <param name="Target">
/// The index of the target closed definition, for <see cref="ArgumentKind.Resolved"/>. The index of the collection in
/// <see cref="ContainerPlan.Collections"/>, for <see cref="ArgumentKind.Collection"/>. The index of the wrapper in
/// <see cref="ContainerPlan.Wrappers"/>, for <see cref="ArgumentKind.Wrapper"/>.
/// </param>
/// <param name="Key">The key that the site asks for, after <c>InheritKey</c>. The fallback provider uses it.</param>
internal sealed record ArgumentPlan(ArgumentKind Kind, int Target, KeyRef? Key = null);

/// <summary>A service type of a skipped instance definition, with the canonical string of its key.</summary>
/// <param name="Type">The service type or the implementation type.</param>
/// <param name="Key">The canonical string of the key, the empty string, or <see cref="AnyKey"/>.</param>
internal readonly record struct ErrorNode(TypeRef Type, string Key)
{
    /// <summary>The key of a declaration whose key is invalid: the key is unknown, so the node matches every key.</summary>
    public const string AnyKey = "*";
}

/// <summary>The construction plan of one closed definition (Analysis.Model.spec, "Construction plan").</summary>
/// <param name="Definition">The closed definition.</param>
/// <param name="Arguments">The arguments, in the order of <see cref="ClosedDefinition.Dependencies"/>.</param>
internal sealed record DefinitionPlan(ClosedDefinition Definition, ImmutableEquatableArray<ArgumentPlan> Arguments);

/// <summary>The resolution of one partial accessor.</summary>
/// <param name="Accessor">The partial accessor.</param>
/// <param name="Argument">How the accessor gets the service.</param>
internal sealed record AccessorPlan(PartialAccessor Accessor, ArgumentPlan Argument);

/// <summary>An item of the lookup table (Container.Output.spec, "Contents of the table").</summary>
/// <param name="ServiceType">The service type <c>S</c>.</param>
/// <param name="Key">The key, or <see langword="null"/> for no key.</param>
/// <param name="Candidates">The indexes of the closed definitions that provide it, in their ordering.</param>
internal sealed record LookupEntry(TypeRef ServiceType, KeyRef? Key, ImmutableEquatableArray<int> Candidates);

/// <summary>Where the container caches a collection (Container.Output.spec, "Collections and dictionaries").</summary>
internal enum CollectionCache
{
    /// <summary>Each request creates a new collection: it has a Transient element, or it merges with the fallback provider.</summary>
    None,

    /// <summary>All elements are Singleton: the root caches one.</summary>
    Root,

    /// <summary>The elements are Singleton or Scoped, with a Scoped one: each scope caches one.</summary>
    Scope,
}

/// <summary>
/// The resolver of a collection whose element is the service itself, or the service in <c>Lazy</c>, <c>Func</c> or
/// <c>Task</c> (Analysis.Model.spec, "Multiple services"). The container supplies all collection types of the grammar as
/// an array of the element.
/// </summary>
/// <param name="Element">The type of the elements: the service type, or the wrapper type of the service.</param>
/// <param name="Key">The key, or <see langword="null"/> for no key and for <paramref name="AnyKey"/>.</param>
/// <param name="AnyKey">Whether it holds all keyed registrations (<c>KeyedService.AnyKey</c>).</param>
/// <param name="Candidates">The indexes of the closed definitions, in their ordering.</param>
/// <param name="Types">
/// The collection types other than <c>IEnumerable&lt;T&gt;</c> that a dependency site asks for, by canonical string. The
/// lookup table gives <c>IEnumerable&lt;T&gt;</c> and these.
/// </param>
/// <param name="Cache">Where the container caches it.</param>
/// <param name="Wrapper">
/// The element taken apart by the grammar, when the element is a wrapper of the service. Such a collection does not
/// merge with the fallback provider (Container.Output.spec, "Behavior for each request").
/// </param>
/// <param name="ElementWrappers">For a collection of wrappers: the index of the wrapper of each candidate in <see cref="ContainerPlan.Wrappers"/>.</param>
internal sealed record CollectionPlan(
    TypeRef Element,
    KeyRef? Key,
    bool AnyKey,
    ImmutableEquatableArray<int> Candidates,
    ImmutableEquatableArray<TypeRef> Types,
    CollectionCache Cache,
    WrapperRequest? Wrapper = null,
    ImmutableEquatableArray<int>? ElementWrappers = null)
{
    /// <summary>The identity of the key in names: the empty string for no key, <c>*</c> for all keyed.</summary>
    public string KeyIdentity => AnyKey ? ErrorNode.AnyKey : KeyRef.CanonicalOf(Key);
}

/// <summary>Where the container caches a wrapper (Container.Output.spec, "Wrapper types").</summary>
internal enum WrapperCache
{
    /// <summary>Each injection creates a new one.</summary>
    None,

    /// <summary>The service is Singleton: the root caches one.</summary>
    Root,

    /// <summary>The service is Scoped: each scope caches one.</summary>
    Scope,

    /// <summary>A <c>Func</c> of any other service: each instance (root or scope) caches one.</summary>
    Instance,
}

/// <summary>
/// The resolver of <c>Lazy&lt;T&gt;</c>, <c>Func&lt;T&gt;</c>, <c>Task&lt;T&gt;</c>, or a nested combination of them
/// (Container.Output.spec, "Wrapper types").
/// </summary>
/// <param name="Type">The wrapper type.</param>
/// <param name="Request">The wrapper type, taken apart by the grammar.</param>
/// <param name="Source">
/// How the wrapper gets <c>T</c>: <see cref="ArgumentKind.Resolved"/>, <see cref="ArgumentKind.Self"/>,
/// <see cref="ArgumentKind.Root"/>, or <see cref="ArgumentKind.Fallback"/>.
/// </param>
/// <param name="Target">The index of the closed definition, for <see cref="ArgumentKind.Resolved"/>.</param>
/// <param name="Key">The key that the site asks for. The lookup table and the fallback provider use it.</param>
/// <param name="Required">For the fallback provider: whether a missing <c>T</c> throws. Otherwise <c>T</c> is <see langword="null"/>.</param>
/// <param name="Cache">Where the container caches it.</param>
/// <param name="Identity">The identity of the wrapper in names and in the order of the wrappers.</param>
/// <param name="Site">
/// Whether a dependency site asks for it. Otherwise it is only the element of a collection. The candidates of a
/// collection can have the same wrapper type and key, so its entry in the lookup table follows the rule of a single service.
/// </param>
/// <param name="Mapping">For <c>Func&lt;A1, ..., An, T&gt;</c>: the constructor parameter of <c>T</c> that each argument supplies.</param>
/// <param name="Overrides">
/// For <c>Func&lt;A1, ..., An, T&gt;</c>: the arguments of <c>T</c> that differ from its own resolver. A parameter that
/// another <c>Func</c> maps is <see cref="ArgumentKind.Unavailable"/> in the resolver of <c>T</c>, and this <c>Func</c>
/// resolves it as usual.
/// </param>
internal sealed record WrapperPlan(
    TypeRef Type,
    WrapperRequest Request,
    ArgumentKind Source,
    int Target,
    KeyRef? Key,
    bool Required,
    WrapperCache Cache,
    string Identity,
    bool Site,
    ImmutableEquatableArray<int>? Mapping = null,
    ImmutableEquatableArray<ArgumentOverride>? Overrides = null);

/// <summary>An argument of a closed definition that replaces the argument of its own resolver.</summary>
/// <param name="Index">The index in <see cref="ClosedDefinition.Dependencies"/>.</param>
/// <param name="Argument">The argument.</param>
internal sealed record ArgumentOverride(int Index, ArgumentPlan Argument);

/// <summary>
/// The resolver of a <c>KeyValuePair&lt;TKey, T&gt;</c>: the key of the dependency site with the value. A pair is a value
/// type, so it is created each time (Container.Output.spec, "Wrapper types"). The value follows its own rules.
/// </summary>
/// <param name="Type">The pair type.</param>
/// <param name="Request">The pair type, taken apart by the grammar.</param>
/// <param name="Key">The key of the dependency site, which is also the key of the registration.</param>
/// <param name="Value">The value: <see cref="ArgumentKind.Resolved"/>, or <see cref="ArgumentKind.Wrapper"/>.</param>
/// <param name="Identity">The identity of the pair in names and in the order of the pairs.</param>
internal sealed record PairPlan(TypeRef Type, WrapperRequest Request, KeyRef Key, ArgumentPlan Value, string Identity)
{
    /// <summary>Gets the type of the value of the pair.</summary>
    public TypeRef ValueType => ((NamedTypeRef)Type).Arguments![1];
}

/// <summary>
/// The resolver of a collection of <c>KeyValuePair&lt;TKey, T&gt;</c> (an array of pairs) or of a dictionary (a
/// <c>Dictionary&lt;TKey, T&gt;</c>): the keyed registrations whose key type is <c>TKey</c>, each with its own key
/// (Analysis.Model.spec, "Multiple services"). It does not merge with the fallback provider.
/// </summary>
/// <param name="Request">The requested type, taken apart by the grammar.</param>
/// <param name="ValueType">The type of each value: the service, or a wrapper of it.</param>
/// <param name="Key">The key of the dependency site, or <see langword="null"/> for all keyed (no key or <c>AnyKey</c>).</param>
/// <param name="Candidates">The indexes of the closed definitions, in their ordering.</param>
/// <param name="Values">The value of each candidate: <see cref="ArgumentKind.Resolved"/> or <see cref="ArgumentKind.Wrapper"/>.</param>
/// <param name="Types">The types that the dependency sites ask for, by canonical string.</param>
/// <param name="Cache">Where the container caches it.</param>
/// <param name="Identity">The identity in names and in the order of the resolvers.</param>
internal sealed record KeyedCollectionPlan(
    WrapperRequest Request,
    TypeRef ValueType,
    KeyRef? Key,
    ImmutableEquatableArray<int> Candidates,
    ImmutableEquatableArray<ArgumentPlan> Values,
    ImmutableEquatableArray<TypeRef> Types,
    CollectionCache Cache,
    string Identity)
{
    /// <summary>Gets whether it is a dictionary. Otherwise it is a collection of pairs.</summary>
    public bool IsDictionary => Request.Shape == RequestShape.Dictionary;
}

/// <summary>Everything that the code of a container needs.</summary>
/// <param name="Container">The container class.</param>
/// <param name="Definitions">The construction plans, in the ordering of the closed definitions.</param>
/// <param name="Lookup">The service types of the lookup table, by canonical string.</param>
/// <param name="TopologicalOrder">The indexes of the closed definitions, each dependency before the definitions that depend on it.</param>
/// <param name="Accessors">The partial accessors.</param>
/// <param name="Collections">The collections, by the canonical string of the element and the key.</param>
/// <param name="Wrappers">The wrappers that the dependency sites ask for, by their identity.</param>
/// <param name="Pairs">The <c>KeyValuePair</c> types that the dependency sites ask for, by their identity.</param>
/// <param name="KeyedCollections">The collections of pairs and the dictionaries that the dependency sites ask for.</param>
internal sealed record ContainerPlan(
    ContainerModel Container,
    ImmutableEquatableArray<DefinitionPlan> Definitions,
    ImmutableEquatableArray<LookupEntry> Lookup,
    ImmutableEquatableArray<int> TopologicalOrder,
    ImmutableEquatableArray<AccessorPlan> Accessors,
    ImmutableEquatableArray<CollectionPlan> Collections,
    ImmutableEquatableArray<WrapperPlan> Wrappers,
    ImmutableEquatableArray<PairPlan> Pairs,
    ImmutableEquatableArray<KeyedCollectionPlan> KeyedCollections);

/// <summary>A problem that the full check finds. The position is added from the site of the container.</summary>
/// <param name="Id">The diagnostic ID.</param>
/// <param name="Arguments">The message arguments.</param>
/// <param name="Source">
/// For a problem that the definite check also finds, the source that owns the dependency site. The full check does not
/// report it again when the definite check reports it (Analysis.Model.spec, "Two levels of checks").
/// </param>
/// <param name="Site">That dependency site.</param>
internal sealed record ContainerProblem(string Id, ImmutableEquatableArray<string> Arguments, DefinitionSource? Source = null, SitePosition Site = default);

/// <summary>The result of the analysis of one container.</summary>
/// <param name="Type">The container class.</param>
/// <param name="Plan">The plan, or <see langword="null"/> when the container is not generated.</param>
/// <param name="Problems">The problems of the full check.</param>
internal sealed record ContainerAnalysisResult(NamedTypeRef Type, ContainerPlan? Plan, ImmutableEquatableArray<ContainerProblem> Problems);

/// <summary>
/// Resolves the dependencies of the container view and runs the full check of single dependencies
/// (Analysis.Model.spec, "Dependency resolution" and "Two levels of checks").
/// </summary>
internal static class ContainerAnalysis
{
    /// <summary>Analyzes one container.</summary>
    /// <param name="container">The container.</param>
    /// <param name="definitions">The closed definitions of the view, in their ordering.</param>
    /// <param name="errorNodes">The implementation types and the known service types of the skipped instance definitions, with their keys.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The plan and the problems.</returns>
    public static ContainerAnalysisResult Analyze(
        ContainerModel container,
        ImmutableEquatableArray<ClosedDefinition> definitions,
        ImmutableEquatableArray<ErrorNode> errorNodes,
        CancellationToken cancellationToken)
    {
        Dictionary<(TypeRef Type, string Key), List<int>> providers = DependencyGraph.Providers(definitions);
        HashSet<(TypeRef, string)> skipped = [.. errorNodes.Select(static n => (n.Type, n.Key))];
        (List<CollectionPlan> collections, Dictionary<(TypeRef, string), int> collectionIndexes) = Collections(container, definitions, providers);
        List<ContainerProblem> problems = [];
        List<DefinitionPlan> plans = [];
        List<GraphEdge>[] edges = new List<GraphEdge>[definitions.Length];
        List<WrapperPlan> wrappers = [];
        Dictionary<string, int> wrapperIndexes = new(StringComparer.Ordinal);
        List<PairPlan> pairs = [];
        Dictionary<string, int> pairIndexes = new(StringComparer.Ordinal);
        List<KeyedCollectionPlan> keyedCollections = [];
        Dictionary<string, int> keyedIndexes = new(StringComparer.Ordinal);
        HashSet<(TypeRef, string)> duplicateKeys = [];
        bool reachesErrorNode = false;

        // The constructor parameters that a valid Func<A..., T> maps, by closed definition and parameter
        HashSet<(int Definition, int Parameter)> mapped = [];
        foreach((TypeRef type, KeyRef? key) in definitions
            .SelectMany(static d => d.Dependencies.Where(static p => p.KeyMode != SiteKeyMode.ServiceKey).Select(p => (p.Type, p.KeyIn(d))))
            .Concat(container.Accessors.Select(static a => (a.Type, a.Key))))
        {
            if(WrapperRequest.Parse(type) is { IsSingleFuncWithArguments: true } request
                && request.DuplicateArgument() is null
                && providers.TryGetValue((request.Core, KeyRef.CanonicalOf(key)), out List<int>? targets)
                && targets.Count == 1
                && request.Map(definitions[targets[0]], out ImmutableEquatableArray<int> mapping) is null)
            {
                mapped.UnionWith(mapping.Select(m => (targets[0], m)));
            }
        }

        // The parameters that a Func resolves itself, because they are unavailable in the resolver of their closed
        // definition: each one is resolved one time, and its edges are added after the walk of the definitions
        Dictionary<(int Definition, int Parameter), ArgumentPlan?> funcArguments = [];
        List<GraphEdge> funcEdges = [];

        // The element of a collection of wrappers is the wrapper of each candidate, with the cache of that candidate
        for(int c = 0; c < collections.Count; c++)
        {
            if(collections[c] is { Wrapper: { } element } collection)
            {
                collections[c] = collection with
                {
                    ElementWrappers = collection.Candidates
                        .Select(i => Wrap(collection.Element, element, new ArgumentPlan(ArgumentKind.Resolved, i), definitions[i].Key, false, site: false).Target)
                        .ToImmutableEquatableArray(),
                };
            }
        }
        for(int index = 0; index < definitions.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClosedDefinition definition = definitions[index];
            List<ArgumentPlan> arguments = [];
            edges[index] = [];
            for(int p = 0; p < definition.Dependencies.Length; p++)
            {
                Dependency parameter = definition.Dependencies[p];
                if(parameter.KeyMode == SiteKeyMode.ServiceKey)
                {
                    // Without a key, only an optional parameter is left (SGIOC023 skips a required one)
                    arguments.Add(new ArgumentPlan(definition.Key is null ? ArgumentKind.Default : ArgumentKind.ServiceKey, -1));
                    continue;
                }

                // A mapped parameter without a candidate is not missing: the caller of the Func usually supplies it. It
                // goes to the fallback provider without SGIOC501, or it throws (Analysis.Model.spec, "Mapping of Func<..., T>")
                if(MappedWithoutCandidate(index, p))
                {
                    arguments.Add(container.IntegrateServiceProvider
                        ? new ArgumentPlan(ArgumentKind.Fallback, -1, parameter.KeyIn(definition))
                        : new ArgumentPlan(ArgumentKind.Unavailable, -1));
                    continue;
                }

                if(ResolveParameter(index, p) is { } resolved)
                {
                    arguments.Add(resolved);
                    AddEdges(edges[index], index, p, resolved);
                }
            }

            plans.Add(new DefinitionPlan(definition, arguments.ToImmutableEquatableArray()));
        }

        // A partial accessor is a dependency site of the container, not of a closed definition: it adds no edge
        List<AccessorPlan> accessors = [];
        foreach(PartialAccessor accessor in container.Accessors)
        {
            if(WrapperRequest.Parse(accessor.Type) is null)
            {
                problems.Add(new ContainerProblem(Descriptors.UnsupportedWrapper.Id, [TypeNames.Display(accessor.Type)]));
            }
            else if(Resolve(accessor.Type, accessor.Key, accessor.IsOptional, container.Type.SortName, accessor.Name) is { } argument)
            {
                accessors.Add(new AccessorPlan(accessor, argument));
            }
        }

        foreach(GraphEdge edge in funcEdges)
        {
            edges[edge.From].Add(edge);
        }

        CheckGraph(definitions, edges, problems);
        if(reachesErrorNode || problems.Any(static p => !IsWarning(p.Id)))
        {
            return new(container.Type, null, problems.ToImmutableEquatableArray());
        }

        LookupEntry[] lookup = [.. providers
            .OrderBy(static p => p.Key.Type.CanonicalString, StringComparer.Ordinal)
            .ThenBy(static p => p.Key.Key, StringComparer.Ordinal)
            .Select(p => new LookupEntry(p.Key.Type, definitions[p.Value[0]].Key, p.Value.ToImmutableEquatableArray()))];
        ContainerPlan plan = new(
            container,
            plans.ToImmutableEquatableArray(),
            lookup.ToImmutableEquatableArray(),
            TopologicalOrder(plans, collections, wrappers, pairs, keyedCollections),
            accessors.ToImmutableEquatableArray(),
            collections.ToImmutableEquatableArray(),
            wrappers.ToImmutableEquatableArray(),
            pairs.ToImmutableEquatableArray(),
            keyedCollections.ToImmutableEquatableArray());
        return new(container.Type, plan, problems.ToImmutableEquatableArray());

        // A required parameter of a plain service type that a valid Func maps, and that has no candidate
        bool MappedWithoutCandidate(int index, int p)
        {
            ClosedDefinition definition = definitions[index];
            Dependency parameter = definition.Dependencies[p];
            return mapped.Contains((index, p))
                && parameter.Optional == OptionalMode.Required
                && WrapperRequest.Parse(parameter.Type) is { IsPlain: true }
                && (parameter.KeyIn(definition) is not null || BuiltIn(parameter.Type, container.Type) is null)
                && !providers.ContainsKey((parameter.Type, KeyRef.CanonicalOf(parameter.KeyIn(definition))));
        }

        ArgumentPlan? ResolveParameter(int index, int p)
        {
            ClosedDefinition definition = definitions[index];
            Dependency parameter = definition.Dependencies[p];
            return Resolve(parameter.Type, parameter.KeyIn(definition), parameter.Optional != OptionalMode.Required, KeyNames.Definition(definition), parameter.Name, definition.Source, definition.SiteOf(p));
        }

        // The edges of one argument (Analysis.Model.spec, "Circular dependencies")
        void AddEdges(List<GraphEdge> list, int index, int p, ArgumentPlan resolved)
        {
            // The edge of a pair is the edge of its value
            ArgumentPlan argument = resolved.Kind == ArgumentKind.Pair ? pairs[resolved.Target].Value : resolved;
            if(argument.Kind == ArgumentKind.Resolved)
            {
                list.Add(new GraphEdge(index, p, argument.Target));
            }
            else if(argument.Kind == ArgumentKind.Collection)
            {
                // One edge for each candidate, deferred for Lazy and Func
                CollectionPlan collection = collections[argument.Target];
                DeferredKind deferred = collection.Wrapper?.Deferred ?? DeferredKind.None;
                list.AddRange(collection.Candidates.Select(c => new GraphEdge(index, p, c, deferred)));
            }
            else if(argument.Kind == ArgumentKind.Wrapper && wrappers[argument.Target] is { Source: ArgumentKind.Resolved } wrapper)
            {
                list.Add(new GraphEdge(index, p, wrapper.Target, wrapper.Request.Deferred, wrapper.Mapping));
            }
            else if(argument.Kind == ArgumentKind.Keyed)
            {
                // One edge for each candidate, deferred for Lazy and Func
                KeyedCollectionPlan keyed = keyedCollections[argument.Target];
                list.AddRange(keyed.Candidates.Select(c => new GraphEdge(index, p, c, keyed.Request.Deferred)));
            }
        }

        // A single dependency (Analysis.Model.spec, "Single service"). A problem is recorded, and the result is null.
        // The dependency site in a message is "{owner} → {member}" (Diagnostics.spec, "Names in messages").
        // With a fallback provider, a missing dependency goes to it, and only a required one is reported (SGIOC501).
        // Without one, a missing optional dependency uses its default value.
        // The source and the site of a closed definition let the output skip a problem that the definite
        // check already reports. A partial accessor has neither
        ArgumentPlan? Resolve(TypeRef type, KeyRef? key, bool optional, string owner, string member, DefinitionSource? source = null, SitePosition site = default)
        {
            if(key is null && BuiltIn(type, container.Type) is { } builtIn)
            {
                return new ArgumentPlan(builtIn, -1);
            }

            // SGIOC304 comes from the definite check and from the loop of the partial accessors
            if(WrapperRequest.Parse(type) is not { } request)
            {
                return null;
            }

            string canonical = KeyRef.CanonicalOf(key);
            string requested = key is null ? TypeNames.Display(type) : $"{TypeNames.Display(type)} (Key: {KeyNames.Display(key)})";
            // An error node with this key already has its own error
            if(key is { IsAnyKey: false }
                && (request.IsPlain || request.IsSimpleCollection || request.IsSingleWrapper || request.IsSinglePair || request.IsKeyedCollection)
                && !providers.ContainsKey((request.Core, canonical))
                && !skipped.Contains((request.Core, canonical))
                && !skipped.Contains((request.Core, ErrorNode.AnyKey)))
            {
                // TKey is null for the requests without a key type
                CheckRunTimeKey(request.Core, key, request.KeyType, $"{owner} → {member}", requested);
            }

            if(request.IsSinglePair)
            {
                return ResolvePair(type, request, key, optional, owner, member, requested);
            }

            if(request.IsKeyedCollection)
            {
                return ResolveKeyed(type, request, key);
            }

            if(request.IsSingleFuncWithArguments)
            {
                return ResolveFunc(type, request, key, source, site);
            }

            if(request.IsSimpleCollection)
            {
                return ResolveCollection(WrapperRequest.ElementOf(type)!, request.Core, key);
            }

            if(request.IsSingleWrapper)
            {
                // T itself, then the wrapper around it
                if((key is null && BuiltIn(request.Core, container.Type) is { } builtInCore
                        ? new ArgumentPlan(builtInCore, -1)
                        : ResolveSingle(request.Core, key, optional, owner, member, requested)) is not { } core)
                {
                    return null;
                }

                return core.Kind == ArgumentKind.Default ? core : Wrap(type, request, core, key, optional);
            }

            if(!request.IsPlain)
            {
                // The other wrapper types come in a later slice. The container must create them itself, so until
                // then they stay missing, also when they are optional or the container has a fallback provider.
                problems.Add(new ContainerProblem(Descriptors.MissingDependency.Id, [$"{owner} → {member}", requested]));
                return null;
            }

            return ResolveSingle(type, key, optional, owner, member, requested);
        }

        ArgumentPlan? ResolveSingle(TypeRef type, KeyRef? key, bool optional, string owner, string member, string requested)
        {
            string canonical = KeyRef.CanonicalOf(key);
            List<int>? candidates = providers.TryGetValue((type, canonical), out List<int>? list) ? list : null;
            return Pick(candidates, ReachesErrorNode(type, key), key, key, optional, owner, member, requested);
        }

        // Whether a single service with this key can be a skipped declaration
        bool ReachesErrorNode(TypeRef type, KeyRef? key)
            => skipped.Contains((type, KeyRef.CanonicalOf(key))) || skipped.Contains((type, ErrorNode.AnyKey));

        // A KeyValuePair (Analysis.Model.spec, "Grammar of wrapper types"): the registration with the key of the site,
        // when its key type is TKey. A site without a key has no candidate. A missing pair goes to the fallback provider
        // unchanged and without a key (Container.Output.spec, "Behavior for each request")
        ArgumentPlan? ResolvePair(TypeRef type, WrapperRequest request, KeyRef? key, bool optional, string owner, string member, string requested)
        {
            List<int>? candidates = key is not null
                && providers.TryGetValue((request.Core, KeyRef.CanonicalOf(key)), out List<int>? list)
                && WrapperRequest.MatchesKeyType(definitions[list[0]].Key!, request.KeyType!)
                    ? list
                    : null;
            // The default value and the fallback provider stand for the whole pair
            ArgumentPlan? picked = Pick(candidates, key is not null && ReachesErrorNode(request.Core, key), key, null, optional, owner, member, requested);
            if(picked is not { Kind: ArgumentKind.Resolved } core)
            {
                return picked;
            }

            WrapperRequest value = request.PairValue;
            ArgumentPlan argument = value.IsPlain ? core : Wrap(((NamedTypeRef)type).Arguments![1], value, core, key, optional);
            string identity = $"{type.CanonicalString}|{key!.Canonical}|{definitions[core.Target].Source.CanonicalString}";
            if(!pairIndexes.TryGetValue(identity, out int index))
            {
                index = pairs.Count;
                pairIndexes[identity] = index;
                pairs.Add(new PairPlan(type, request, key, argument, identity));
            }

            return new ArgumentPlan(ArgumentKind.Pair, index, key);
        }

        // A collection of pairs or a dictionary (Analysis.Model.spec, "Multiple services"): the keyed registrations whose
        // key type is TKey. No key and AnyKey ask for all of them, a key only for its registrations. It is never missing
        // and does not merge with the fallback provider (Container.Output.spec, "Behavior for each request"). In a
        // dictionary, one key with more than one candidate is SGIOC404
        ArgumentPlan? ResolveKeyed(TypeRef type, WrapperRequest request, KeyRef? key)
        {
            KeyRef? filter = key is { IsAnyKey: true } ? null : key;
            if(skipped.Any(n => n.Item1.Equals(request.Core) && (filter is null ? n.Item2.Length > 0 : n.Item2 == filter.Canonical || n.Item2 == ErrorNode.AnyKey)))
            {
                reachesErrorNode = true;
                return null;
            }

            TypeRef keyType = request.KeyType!;
            NamedTypeRef keyed = (NamedTypeRef)(WrapperRequest.ElementOf(type) ?? type);
            TypeRef valueType = keyed.Arguments![1];
            string identity = $"{(request.Shape == RequestShape.Dictionary ? "dictionary" : "pairs")}|{keyType.CanonicalString}|{valueType.CanonicalString}|{KeyRef.CanonicalOf(filter)}";
            if(keyedIndexes.TryGetValue(identity, out int index))
            {
                KeyedCollectionPlan existing = keyedCollections[index];
                if(!existing.Types.Contains(type))
                {
                    keyedCollections[index] = existing with { Types = existing.Types.Append(type).OrderBy(static t => t.CanonicalString, StringComparer.Ordinal).ToImmutableEquatableArray() };
                }

                return new ArgumentPlan(ArgumentKind.Keyed, index, key);
            }

            int[] candidates = [.. providers
                .Where(p => p.Key.Type.Equals(request.Core)
                    && p.Key.Key.Length > 0
                    && (filter is null || p.Key.Key == filter.Canonical)
                    && WrapperRequest.MatchesKeyType(definitions[p.Value[0]].Key!, keyType))
                .SelectMany(static p => p.Value)
                .OrderBy(static i => i)];
            if(request.Shape == RequestShape.Dictionary)
            {
                // One report for each service type and key: the message has no site, and a key implies its key type
                foreach(IGrouping<string, int> duplicate in candidates
                    .GroupBy(c => definitions[c].Key!.Canonical)
                    .Where(g => g.Count() > 1 && duplicateKeys.Add((request.Core, g.Key))))
                {
                    problems.Add(new ContainerProblem(
                        Descriptors.DuplicateDictionaryKey.Id,
                        [TypeNames.Display(request.Core), KeyNames.Display(definitions[duplicate.First()].Key!), string.Join(", ", duplicate.Select(c => KeyNames.Definition(definitions[c])))]));
                }
            }

            // The value of each candidate is the service or its wrapper. The wrapper of an element follows the rule of a
            // single service in the lookup table, the same as for a collection of wrappers
            WrapperRequest value = request.PairValue;
            ArgumentPlan[] values = [.. candidates.Select(c => value.IsPlain
                ? new ArgumentPlan(ArgumentKind.Resolved, c, definitions[c].Key)
                : Wrap(valueType, value, new ArgumentPlan(ArgumentKind.Resolved, c), definitions[c].Key, false, site: false))];
            index = keyedCollections.Count;
            keyedIndexes[identity] = index;
            keyedCollections.Add(new KeyedCollectionPlan(
                request,
                valueType,
                filter,
                candidates.ToImmutableEquatableArray(),
                values.ToImmutableEquatableArray(),
                [type],
                ElementCache(candidates, definitions),
                identity));
            return new ArgumentPlan(ArgumentKind.Keyed, index, key);
        }

        // Func<A1, ..., An, T> (Analysis.Model.spec, "Mapping of Func<..., T>"): T resolves to one Transient closed
        // definition, and each argument maps to one constructor parameter of it. Otherwise SGIOC305, also when T is missing
        // or comes only from the fallback provider. The definite check reports some of these at the same site
        ArgumentPlan? ResolveFunc(TypeRef type, WrapperRequest request, KeyRef? key, DefinitionSource? source, SitePosition site)
        {
            string service = key is null ? TypeNames.Display(request.Core) : $"{TypeNames.Display(request.Core)} (Key: {KeyNames.Display(key)})";
            string? reason = request.DuplicateArgument();
            if(reason is null)
            {
                if(key is null && BuiltIn(request.Core, container.Type) is not null)
                {
                    reason = $"'{service}' is a built-in service";
                }
                else if(providers.TryGetValue((request.Core, KeyRef.CanonicalOf(key)), out List<int>? candidates))
                {
                    if(candidates.Count > 1)
                    {
                        reason = $"'{service}' is ambiguous: {string.Join(", ", candidates.Select(c => KeyNames.Definition(definitions[c])))}";
                    }
                    else if(request.Map(definitions[candidates[0]], out ImmutableEquatableArray<int> mapping) is { } invalid)
                    {
                        reason = invalid;
                    }
                    else
                    {
                        return Wrap(type, request, new ArgumentPlan(ArgumentKind.Resolved, candidates[0], key), key, false, mapping: mapping, overrides: Overrides(candidates[0], mapping));
                    }
                }
                else if(ReachesErrorNode(request.Core, key))
                {
                    reachesErrorNode = true;
                    return null;
                }
                else
                {
                    reason = container.IntegrateServiceProvider ? $"'{service}' is not registered in the container" : $"'{service}' is not registered";
                }
            }

            problems.Add(new ContainerProblem(Descriptors.InvalidFuncWithArguments.Id, [TypeNames.Display(type), reason], source, site));
            return null;
        }

        // The parameters of the target that another Func maps and that this Func does not map: this Func resolves them as
        // usual. Each one is resolved one time, so its problems are reported one time
        ImmutableEquatableArray<ArgumentOverride> Overrides(int target, ImmutableEquatableArray<int> mapping)
        {
            List<ArgumentOverride> overrides = [];
            for(int p = 0; p < definitions[target].ConstructorParameters.Length; p++)
            {
                if(mapping.Contains(p) || !MappedWithoutCandidate(target, p))
                {
                    continue;
                }

                if(!funcArguments.TryGetValue((target, p), out ArgumentPlan? argument))
                {
                    argument = ResolveParameter(target, p);
                    funcArguments[(target, p)] = argument;
                    if(argument is not null)
                    {
                        AddEdges(funcEdges, target, p, argument);
                    }
                }

                if(argument is not null)
                {
                    overrides.Add(new ArgumentOverride(p, argument));
                }
            }

            return overrides.ToImmutableEquatableArray();
        }

        // The single service rule on the candidates (Analysis.Model.spec, "Single service"). The fallback provider gets
        // fallbackKey: the key of the site, or no key for a KeyValuePair
        ArgumentPlan? Pick(List<int>? candidates, bool errorNode, KeyRef? key, KeyRef? fallbackKey, bool optional, string owner, string member, string requested)
        {
            switch(candidates?.Count ?? 0)
            {
                case 1:
                    return new ArgumentPlan(ArgumentKind.Resolved, candidates![0], key);
                case 0 when errorNode:
                    // An error node: the original error is already reported, and the build fails because of it
                    reachesErrorNode = true;
                    return null;
                case 0 when container.IntegrateServiceProvider:
                    if(!optional)
                    {
                        problems.Add(new ContainerProblem(Descriptors.FallbackDependency.Id, [$"{owner} → {member}", requested]));
                    }

                    return new ArgumentPlan(ArgumentKind.Fallback, -1, fallbackKey);
                case 0 when optional:
                    return new ArgumentPlan(ArgumentKind.Default, -1);
                case 0:
                    problems.Add(new ContainerProblem(Descriptors.MissingDependency.Id, [$"{owner} → {member}", requested]));
                    return null;
                default:
                    problems.Add(new ContainerProblem(
                        Descriptors.AmbiguousDependency.Id,
                        [$"{owner} → {member}", requested, candidates!.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Join(", ", candidates.Select(c => KeyNames.Definition(definitions[c])))]));
                    return null;
            }
        }

        // A collection is never missing: without candidates it is empty (Analysis.Model.spec, "Multiple services").
        // A candidate that is an error node makes it incomplete, so the container is not generated.
        ArgumentPlan? ResolveCollection(TypeRef element, TypeRef service, KeyRef? key)
        {
            if(skipped.Any(n => n.Item1.Equals(service) && (key is { IsAnyKey: true } ? n.Item2.Length > 0 : n.Item2 == KeyRef.CanonicalOf(key) || n.Item2 == ErrorNode.AnyKey)))
            {
                reachesErrorNode = true;
                return null;
            }

            return new ArgumentPlan(ArgumentKind.Collection, collectionIndexes[(element, CollectionKey(key))], key);
        }

        // The wrapper around T (Container.Output.spec, "Wrapper types"). Its cache follows the lifetime of T. A service of
        // the fallback provider and a built-in service count as Transient. Sites and collections that ask for the same
        // wrapper share it
        ArgumentPlan Wrap(TypeRef type, WrapperRequest request, ArgumentPlan core, KeyRef? key, bool optional, bool site = true, ImmutableEquatableArray<int>? mapping = null, ImmutableEquatableArray<ArgumentOverride>? overrides = null)
        {
            Lifetime lifetime = core.Kind == ArgumentKind.Resolved ? definitions[core.Target].Lifetime : Lifetime.Transient;
            WrapperCache cache = lifetime switch
            {
                Lifetime.Singleton => WrapperCache.Root,
                Lifetime.Scoped => WrapperCache.Scope,
                _ => request.Deferred == DeferredKind.Func ? WrapperCache.Instance : WrapperCache.None,
            };
            bool required = core.Kind != ArgumentKind.Fallback || !optional;
            string identity = core.Kind switch
            {
                ArgumentKind.Resolved => $"{type.CanonicalString}|{definitions[core.Target].Source.CanonicalString}|{KeyRef.CanonicalOf(definitions[core.Target].Key)}",
                ArgumentKind.Fallback => $"{type.CanonicalString}|fallback|{KeyRef.CanonicalOf(key)}|{(required ? "required" : "optional")}",
                _ => $"{type.CanonicalString}|{core.Kind}",
            };
            if(!wrapperIndexes.TryGetValue(identity, out int index))
            {
                index = wrappers.Count;
                wrapperIndexes[identity] = index;
                wrappers.Add(new WrapperPlan(type, request, core.Kind, core.Target, key, required, cache, identity, site, mapping, overrides));
            }
            else if(site && !wrappers[index].Site)
            {
                wrappers[index] = wrappers[index] with { Site = true };
            }

            return new ArgumentPlan(ArgumentKind.Wrapper, index, key);
        }

        // SGIOC505: the key of the site has no registration of the same compile-time identity, but the key of a
        // registration of the same service type can be equal to it at run time (Semantics.spec, "Keyed services"). For a
        // KeyValuePair, only the registrations whose key type is TKey count
        void CheckRunTimeKey(TypeRef service, KeyRef key, TypeRef? keyType, string site, string requested)
        {
            string[] registrations = [.. providers
                .Where(p => p.Key.Type.Equals(service)
                    && p.Key.Key.Length > 0
                    && MayBeEqual(key, definitions[p.Value[0]].Key!)
                    && (keyType is null || WrapperRequest.MatchesKeyType(definitions[p.Value[0]].Key!, keyType)))
                .SelectMany(static p => p.Value)
                .OrderBy(static i => i)
                .Select(i => KeyNames.Definition(definitions[i]))];
            if(registrations.Length > 0)
            {
                problems.Add(new ContainerProblem(Descriptors.KeyMatchesOnlyAtRunTime.Id, [site, requested, string.Join(", ", registrations)]));
            }
        }
    }

    /// <summary>
    /// Whether two keys with different compile-time identities can be equal at run time: at least one of them is a
    /// symbol, and their key types are the same or one of them is <c>object</c>. Two different values are never equal.
    /// </summary>
    internal static bool MayBeEqual(KeyRef site, KeyRef registered)
        => (site.Kind == KeyKind.Symbol || registered.Kind == KeyKind.Symbol)
            && (site.ValueType.Equals(registered.ValueType) || IsObject(site.ValueType) || IsObject(registered.ValueType));

    private static bool IsObject(TypeRef type) => type is NamedTypeRef { MetadataName: "System.Object" };

    /// <summary>The identity of the key of a collection: the empty string for no key, <c>*</c> for <c>AnyKey</c>.</summary>
    private static string CollectionKey(KeyRef? key) => key is { IsAnyKey: true } ? ErrorNode.AnyKey : KeyRef.CanonicalOf(key);

    /// <summary>
    /// The collections of the view: <c>IEnumerable&lt;S&gt;</c> of each registered service type <c>S</c> (no key, each
    /// key, and all keyed), and each collection that a dependency site asks for, also of a wrapper of <c>S</c>
    /// (Container.Output.spec, "Contents of the table"). They are ordered by the canonical string of the element, then
    /// of the key. The wrappers of the elements are added later.
    /// </summary>
    private static (List<CollectionPlan>, Dictionary<(TypeRef, string), int>) Collections(
        ContainerModel container,
        ImmutableEquatableArray<ClosedDefinition> definitions,
        Dictionary<(TypeRef Type, string Key), List<int>> providers)
    {
        Dictionary<(TypeRef Element, string Key), (KeyRef? Key, SortedDictionary<string, TypeRef> Types, WrapperRequest? Wrapper)> found = [];
        foreach((TypeRef type, string key) in providers.Keys)
        {
            Add(type, null, null, null);
            if(key.Length > 0)
            {
                Add(type, null, definitions[providers[(type, key)][0]].Key, null);
                Add(type, null, null, null, anyKey: true);
            }
        }

        foreach(ClosedDefinition definition in definitions)
        {
            foreach(Dependency dependency in definition.Dependencies.Where(static d => d.KeyMode != SiteKeyMode.ServiceKey))
            {
                AddDemand(dependency.Type, dependency.KeyIn(definition));
            }
        }

        foreach(PartialAccessor accessor in container.Accessors)
        {
            AddDemand(accessor.Type, accessor.Key);
        }

        List<CollectionPlan> collections = [];
        Dictionary<(TypeRef, string), int> indexes = [];
        foreach(KeyValuePair<(TypeRef Element, string Key), (KeyRef? Key, SortedDictionary<string, TypeRef> Types, WrapperRequest? Wrapper)> item in found
            .OrderBy(static c => c.Key.Element.CanonicalString, StringComparer.Ordinal)
            .ThenBy(static c => c.Key.Key, StringComparer.Ordinal))
        {
            (TypeRef element, string identity) = item.Key;
            WrapperRequest? wrapper = item.Value.Wrapper;
            TypeRef service = wrapper?.Core ?? element;
            bool anyKey = identity == ErrorNode.AnyKey;
            int[] candidates = anyKey
                ? [.. providers.Where(p => p.Key.Type.Equals(service) && p.Key.Key.Length > 0).SelectMany(static p => p.Value).OrderBy(static i => i)]
                : providers.TryGetValue((service, identity), out List<int>? list) ? [.. list] : [];
            indexes[item.Key] = collections.Count;
            collections.Add(new CollectionPlan(
                element,
                item.Value.Key,
                anyKey,
                candidates.ToImmutableEquatableArray(),
                item.Value.Types.Values.ToImmutableEquatableArray(),
                Cache(candidates, merges: wrapper is null && container.IntegrateServiceProvider),
                wrapper));
        }

        return (collections, indexes);

        void AddDemand(TypeRef type, KeyRef? key)
        {
            if(WrapperRequest.Parse(type) is { IsSimpleCollection: true } request)
            {
                Add(
                    WrapperRequest.ElementOf(type)!,
                    type is NamedTypeRef { MetadataName: WrapperRequest.Enumerable } ? null : type,
                    key is { IsAnyKey: true } ? null : key,
                    request.IsWrappedElement ? request : null,
                    key is { IsAnyKey: true });
            }
        }

        void Add(TypeRef element, TypeRef? type, KeyRef? key, WrapperRequest? wrapper, bool anyKey = false)
        {
            (TypeRef, string) identity = (element, anyKey ? ErrorNode.AnyKey : KeyRef.CanonicalOf(key));
            if(!found.TryGetValue(identity, out (KeyRef?, SortedDictionary<string, TypeRef> Types, WrapperRequest?) value))
            {
                found[identity] = value = (key, new SortedDictionary<string, TypeRef>(StringComparer.Ordinal), wrapper);
            }

            if(type is not null)
            {
                value.Types[type.CanonicalString] = type;
            }
        }

        // A merge with the fallback provider and a Transient element need a new collection for each request. A
        // collection of wrappers does not merge
        CollectionCache Cache(int[] candidates, bool merges) => merges ? CollectionCache.None : ElementCache(candidates, definitions);
    }

    /// <summary>
    /// Where the container caches a collection that does not merge with the fallback provider (Container.Output.spec,
    /// "Collections and dictionaries"): each request creates one when it is empty or has a Transient element.
    /// </summary>
    private static CollectionCache ElementCache(int[] candidates, ImmutableEquatableArray<ClosedDefinition> definitions)
    {
        if(candidates.Length == 0 || candidates.Any(c => definitions[c].Lifetime == Lifetime.Transient))
        {
            return CollectionCache.None;
        }

        return candidates.All(c => definitions[c].Lifetime == Lifetime.Singleton) ? CollectionCache.Root : CollectionCache.Scope;
    }

    /// <summary>
    /// The full check of the dependency graph, on the edges of the resolved arguments: a built-in service, also this
    /// container class, is not a node. Each problem keeps its dependency site. The output does not report a problem
    /// that the definite check already reported with the same ID at the same site (Analysis.Model.spec, "Two levels of
    /// checks"), but the problem still stops the generation.
    /// </summary>
    private static void CheckGraph(ImmutableEquatableArray<ClosedDefinition> definitions, List<GraphEdge>[] edges, List<ContainerProblem> problems)
    {
        problems.AddRange(DependencyGraph.UnsupportedWrappers(definitions)
            .Concat(DependencyGraph.Cycles(definitions, edges))
            .Concat(DependencyGraph.Captives(definitions, edges))
            .Concat(DependencyGraph.HeldTransients(definitions, edges))
            .Select(static p => new ContainerProblem(p.Id, p.Arguments, p.Source, p.Site)));

        // Disposable Transient is only in the full check (Analysis.Model.spec, "Disposable Transient")
        foreach(ClosedDefinition definition in definitions.Where(static d => d.Lifetime == Lifetime.Transient && d.Disposal != Disposal.None))
        {
            string kind = definition.Disposal switch
            {
                Disposal.Sync => "IDisposable",
                Disposal.Async => "IAsyncDisposable",
                _ => "IDisposable, IAsyncDisposable",
            };
            problems.Add(new ContainerProblem(Descriptors.DisposableTransient.Id, [KeyNames.Definition(definition), kind]));
        }
    }

    /// <summary>Whether a diagnostic is a Warning, which has no effect on generation.</summary>
    /// <param name="id">The diagnostic ID.</param>
    /// <returns>Whether the descriptor is a Warning or an Info.</returns>
    public static bool IsWarning(string id) => Descriptors.ById[id].DefaultSeverity != DiagnosticSeverity.Error;

    /// <summary>The built-in services (Analysis.Model.spec, "Built-in services").</summary>
    private static ArgumentKind? BuiltIn(TypeRef type, NamedTypeRef container)
    {
        if(type.Equals(container) || BuiltInServices.IsServiceProvider(type))
        {
            return ArgumentKind.Self;
        }

        return type is NamedTypeRef { Arguments: null } named && BuiltInServices.IsRootService(named.MetadataName) ? ArgumentKind.Root : null;
    }


    /// <summary>
    /// Each dependency before the definitions that depend on it. Definitions without a relation follow the ordering.
    /// A cycle (SGIOC301) stops the generation before this walk. Each definition is visited once in any case. The walk
    /// keeps its own stack, so a long dependency chain does not overflow the call stack.
    /// </summary>
    private static ImmutableEquatableArray<int> TopologicalOrder(List<DefinitionPlan> plans, List<CollectionPlan> collections, List<WrapperPlan> wrappers, List<PairPlan> pairs, List<KeyedCollectionPlan> keyedCollections)
    {
        List<int> order = [];
        bool[] visited = new bool[plans.Count];
        Stack<(int Index, bool Done)> walk = new();
        List<int> dependencies = [];
        for(int i = plans.Count - 1; i >= 0; i--)
        {
            walk.Push((i, false));
        }

        while(walk.Count > 0)
        {
            (int index, bool done) = walk.Pop();
            if(done)
            {
                order.Add(index);
                continue;
            }

            if(visited[index])
            {
                continue;
            }

            visited[index] = true;
            walk.Push((index, true));

            // Push in reverse, so the dependencies are visited in the order of the arguments
            dependencies.Clear();
            dependencies.AddRange(Dependencies(plans[index]));
            for(int d = dependencies.Count - 1; d >= 0; d--)
            {
                walk.Push((dependencies[d], false));
            }
        }

        return order.ToImmutableEquatableArray();

        IEnumerable<int> Dependencies(DefinitionPlan plan)
        {
            foreach(ArgumentPlan resolved in plan.Arguments)
            {
                // A pair creates its value at once
                ArgumentPlan argument = resolved.Kind == ArgumentKind.Pair ? pairs[resolved.Target].Value : resolved;
                if(argument.Kind == ArgumentKind.Resolved)
                {
                    yield return argument.Target;
                }
                else if(argument.Kind is ArgumentKind.Collection or ArgumentKind.Keyed)
                {
                    foreach(int candidate in argument.Kind == ArgumentKind.Collection ? collections[argument.Target].Candidates : keyedCollections[argument.Target].Candidates)
                    {
                        yield return candidate;
                    }
                }
                else if(argument.Kind == ArgumentKind.Wrapper && wrappers[argument.Target] is { Source: ArgumentKind.Resolved, Request.Deferred: DeferredKind.None } task)
                {
                    // Task<T> creates T at once. Lazy and Func create it later
                    yield return task.Target;
                }
            }
        }
    }
}
