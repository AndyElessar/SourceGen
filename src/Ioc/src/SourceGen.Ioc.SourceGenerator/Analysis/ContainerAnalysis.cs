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
}

/// <summary>The resolution result of one dependency site.</summary>
/// <param name="Kind">How the argument is resolved.</param>
/// <param name="Target">
/// The index of the target closed definition, for <see cref="ArgumentKind.Resolved"/>. The index of the collection in
/// <see cref="ContainerPlan.Collections"/>, for <see cref="ArgumentKind.Collection"/>.
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
/// The resolver of a collection whose element is the service itself (Analysis.Model.spec, "Multiple services"). The
/// container supplies all collection types of the grammar as an array of the element.
/// </summary>
/// <param name="Element">The service type of the elements.</param>
/// <param name="Key">The key, or <see langword="null"/> for no key and for <paramref name="AnyKey"/>.</param>
/// <param name="AnyKey">Whether it holds all keyed registrations (<c>KeyedService.AnyKey</c>).</param>
/// <param name="Candidates">The indexes of the closed definitions, in their ordering.</param>
/// <param name="Types">
/// The collection types other than <c>IEnumerable&lt;T&gt;</c> that a dependency site asks for, by canonical string. The
/// lookup table gives <c>IEnumerable&lt;T&gt;</c> and these.
/// </param>
/// <param name="Cache">Where the container caches it.</param>
internal sealed record CollectionPlan(
    TypeRef Element,
    KeyRef? Key,
    bool AnyKey,
    ImmutableEquatableArray<int> Candidates,
    ImmutableEquatableArray<TypeRef> Types,
    CollectionCache Cache)
{
    /// <summary>The identity of the key in names: the empty string for no key, <c>*</c> for all keyed.</summary>
    public string KeyIdentity => AnyKey ? ErrorNode.AnyKey : KeyRef.CanonicalOf(Key);
}

/// <summary>Everything that the code of a container needs.</summary>
/// <param name="Container">The container class.</param>
/// <param name="Definitions">The construction plans, in the ordering of the closed definitions.</param>
/// <param name="Lookup">The service types of the lookup table, by canonical string.</param>
/// <param name="TopologicalOrder">The indexes of the closed definitions, each dependency before the definitions that depend on it.</param>
/// <param name="Accessors">The partial accessors.</param>
/// <param name="Collections">The collections, by the canonical string of the element and the key.</param>
internal sealed record ContainerPlan(
    ContainerModel Container,
    ImmutableEquatableArray<DefinitionPlan> Definitions,
    ImmutableEquatableArray<LookupEntry> Lookup,
    ImmutableEquatableArray<int> TopologicalOrder,
    ImmutableEquatableArray<AccessorPlan> Accessors,
    ImmutableEquatableArray<CollectionPlan> Collections);

/// <summary>A problem that the full check finds. The position is added from the site of the container.</summary>
/// <param name="Id">The diagnostic ID.</param>
/// <param name="Arguments">The message arguments.</param>
/// <param name="Implementation">
/// For a problem that the definite check also finds, the implementation type of the dependency site. The full check
/// does not report it again when the definite check reports it (Analysis.Model.spec, "Two levels of checks").
/// </param>
/// <param name="Site">That dependency site.</param>
internal sealed record ContainerProblem(string Id, ImmutableEquatableArray<string> Arguments, NamedTypeRef? Implementation = null, SitePosition Site = default);

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
        bool reachesErrorNode = false;
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

                if(Resolve(parameter.Type, parameter.KeyIn(definition), parameter.Optional != OptionalMode.Required, KeyNames.Definition(definition), parameter.Name) is { } argument)
                {
                    arguments.Add(argument);
                    if(argument.Kind == ArgumentKind.Resolved)
                    {
                        edges[index].Add(new GraphEdge(index, p, argument.Target));
                    }
                    else if(argument.Kind == ArgumentKind.Collection)
                    {
                        // One edge for each candidate (Analysis.Model.spec, "Circular dependencies")
                        edges[index].AddRange(collections[argument.Target].Candidates.Select(c => new GraphEdge(index, p, c)));
                    }
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

        CheckGraph(definitions, edges, problems);
        if(reachesErrorNode || problems.Any(static p => !IsWarning(p.Id)))
        {
            return new(container.Type, null, problems.ToImmutableEquatableArray());
        }

        LookupEntry[] lookup = [.. providers
            .OrderBy(static p => p.Key.Type.CanonicalString, StringComparer.Ordinal)
            .ThenBy(static p => p.Key.Key, StringComparer.Ordinal)
            .Select(p => new LookupEntry(p.Key.Type, definitions[p.Value[0]].Key, p.Value.ToImmutableEquatableArray()))];
        ContainerPlan plan = new(container, plans.ToImmutableEquatableArray(), lookup.ToImmutableEquatableArray(), TopologicalOrder(plans, collections), accessors.ToImmutableEquatableArray(), collections.ToImmutableEquatableArray());
        return new(container.Type, plan, problems.ToImmutableEquatableArray());

        // A single dependency (Analysis.Model.spec, "Single service"). A problem is recorded, and the result is null.
        // The dependency site in a message is "{owner} → {member}" (Diagnostics.spec, "Names in messages").
        // With a fallback provider, a missing dependency goes to it, and only a required one is reported (SGIOC501).
        // Without one, a missing optional dependency uses its default value.
        ArgumentPlan? Resolve(TypeRef type, KeyRef? key, bool optional, string owner, string member)
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

            if(request.IsPlainCollection)
            {
                return ResolveCollection(request.Core, key);
            }

            string canonical = KeyRef.CanonicalOf(key);
            string requested = key is null ? TypeNames.Display(type) : $"{TypeNames.Display(type)} (Key: {KeyNames.Display(key)})";
            switch(providers.TryGetValue((type, canonical), out List<int>? candidates) ? candidates.Count : 0)
            {
                case 1:
                    return new ArgumentPlan(ArgumentKind.Resolved, candidates![0], key);
                case 0 when skipped.Contains((type, canonical)) || skipped.Contains((type, ErrorNode.AnyKey)):
                    // An error node: the original error is already reported, and the build fails because of it
                    reachesErrorNode = true;
                    return null;
                case 0 when !request.IsPlain:
                    // The other wrapper types come in a later slice. The container must create them itself, so until
                    // then they stay missing, also when they are optional or the container has a fallback provider.
                    problems.Add(new ContainerProblem(Descriptors.MissingDependency.Id, [$"{owner} → {member}", requested]));
                    return null;
                case 0 when container.IntegrateServiceProvider:
                    if(!optional)
                    {
                        problems.Add(new ContainerProblem(Descriptors.FallbackDependency.Id, [$"{owner} → {member}", requested]));
                    }

                    return new ArgumentPlan(ArgumentKind.Fallback, -1, key);
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
        ArgumentPlan? ResolveCollection(TypeRef element, KeyRef? key)
        {
            if(skipped.Any(n => n.Item1.Equals(element) && (key is { IsAnyKey: true } ? n.Item2.Length > 0 : n.Item2 == KeyRef.CanonicalOf(key) || n.Item2 == ErrorNode.AnyKey)))
            {
                reachesErrorNode = true;
                return null;
            }

            return new ArgumentPlan(ArgumentKind.Collection, collectionIndexes[(element, CollectionKey(key))], key);
        }
    }

    /// <summary>The identity of the key of a collection: the empty string for no key, <c>*</c> for <c>AnyKey</c>.</summary>
    private static string CollectionKey(KeyRef? key) => key is { IsAnyKey: true } ? ErrorNode.AnyKey : KeyRef.CanonicalOf(key);

    /// <summary>
    /// The collections of the view: <c>IEnumerable&lt;S&gt;</c> of each registered service type <c>S</c> (no key, each
    /// key, and all keyed), and each collection that a dependency site asks for (Container.Output.spec, "Contents of the
    /// table"). They are ordered by the canonical string of the element, then of the key.
    /// </summary>
    private static (List<CollectionPlan>, Dictionary<(TypeRef, string), int>) Collections(
        ContainerModel container,
        ImmutableEquatableArray<ClosedDefinition> definitions,
        Dictionary<(TypeRef Type, string Key), List<int>> providers)
    {
        Dictionary<(TypeRef Element, string Key), (KeyRef? Key, SortedDictionary<string, TypeRef> Types)> found = [];
        foreach((TypeRef type, string key) in providers.Keys)
        {
            Add(type, null, null);
            if(key.Length > 0)
            {
                Add(type, null, definitions[providers[(type, key)][0]].Key);
                Add(type, null, null, anyKey: true);
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
        foreach(KeyValuePair<(TypeRef Element, string Key), (KeyRef? Key, SortedDictionary<string, TypeRef> Types)> item in found
            .OrderBy(static c => c.Key.Element.CanonicalString, StringComparer.Ordinal)
            .ThenBy(static c => c.Key.Key, StringComparer.Ordinal))
        {
            (TypeRef element, string identity) = item.Key;
            bool anyKey = identity == ErrorNode.AnyKey;
            int[] candidates = anyKey
                ? [.. providers.Where(p => p.Key.Type.Equals(element) && p.Key.Key.Length > 0).SelectMany(static p => p.Value).OrderBy(static i => i)]
                : providers.TryGetValue((element, identity), out List<int>? list) ? [.. list] : [];
            indexes[item.Key] = collections.Count;
            collections.Add(new CollectionPlan(element, item.Value.Key, anyKey, candidates.ToImmutableEquatableArray(), item.Value.Types.Values.ToImmutableEquatableArray(), Cache(candidates)));
        }

        return (collections, indexes);

        void AddDemand(TypeRef type, KeyRef? key)
        {
            if(WrapperRequest.Parse(type) is { IsPlainCollection: true } request)
            {
                Add(request.Core, type is NamedTypeRef { MetadataName: WrapperRequest.Enumerable } ? null : type, key is { IsAnyKey: true } ? null : key, key is { IsAnyKey: true });
            }
        }

        void Add(TypeRef element, TypeRef? type, KeyRef? key, bool anyKey = false)
        {
            (TypeRef, string) identity = (element, anyKey ? ErrorNode.AnyKey : KeyRef.CanonicalOf(key));
            if(!found.TryGetValue(identity, out (KeyRef?, SortedDictionary<string, TypeRef> Types) value))
            {
                found[identity] = value = (key, new SortedDictionary<string, TypeRef>(StringComparer.Ordinal));
            }

            if(type is not null)
            {
                value.Types[type.CanonicalString] = type;
            }
        }

        // A merge with the fallback provider and a Transient element need a new collection for each request
        CollectionCache Cache(int[] candidates)
        {
            if(container.IntegrateServiceProvider || candidates.Length == 0 || candidates.Any(c => definitions[c].Lifetime == Lifetime.Transient))
            {
                return CollectionCache.None;
            }

            return candidates.All(c => definitions[c].Lifetime == Lifetime.Singleton) ? CollectionCache.Root : CollectionCache.Scope;
        }
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
            .Select(static p => new ContainerProblem(p.Id, p.Arguments, p.Implementation, p.Site)));

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
    /// A cycle (SGIOC301) stops the generation before this walk. Each definition is visited once in any case.
    /// </summary>
    private static ImmutableEquatableArray<int> TopologicalOrder(List<DefinitionPlan> plans, List<CollectionPlan> collections)
    {
        List<int> order = [];
        bool[] visited = new bool[plans.Count];
        for(int i = 0; i < plans.Count; i++)
        {
            Visit(i);
        }

        return order.ToImmutableEquatableArray();

        void Visit(int index)
        {
            if(visited[index])
            {
                return;
            }

            visited[index] = true;
            foreach(ArgumentPlan argument in plans[index].Arguments)
            {
                if(argument.Kind == ArgumentKind.Resolved)
                {
                    Visit(argument.Target);
                }
                else if(argument.Kind == ArgumentKind.Collection)
                {
                    foreach(int candidate in collections[argument.Target].Candidates)
                    {
                        Visit(candidate);
                    }
                }
            }

            order.Add(index);
        }
    }
}
