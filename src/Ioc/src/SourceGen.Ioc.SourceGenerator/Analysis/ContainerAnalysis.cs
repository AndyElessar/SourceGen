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
}

/// <summary>The resolution result of one dependency site.</summary>
/// <param name="Kind">How the argument is resolved.</param>
/// <param name="Target">The index of the target closed definition, for <see cref="ArgumentKind.Resolved"/>.</param>
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

/// <summary>Everything that the code of a container needs.</summary>
/// <param name="Container">The container class.</param>
/// <param name="Definitions">The construction plans, in the ordering of the closed definitions.</param>
/// <param name="Lookup">The service types of the lookup table, by canonical string.</param>
/// <param name="TopologicalOrder">The indexes of the closed definitions, each dependency before the definitions that depend on it.</param>
/// <param name="Accessors">The partial accessors.</param>
internal sealed record ContainerPlan(
    ContainerModel Container,
    ImmutableEquatableArray<DefinitionPlan> Definitions,
    ImmutableEquatableArray<LookupEntry> Lookup,
    ImmutableEquatableArray<int> TopologicalOrder,
    ImmutableEquatableArray<AccessorPlan> Accessors);

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
                }
            }

            plans.Add(new DefinitionPlan(definition, arguments.ToImmutableEquatableArray()));
        }

        // A partial accessor is a dependency site of the container, not of a closed definition: it adds no edge
        List<AccessorPlan> accessors = [];
        foreach(PartialAccessor accessor in container.Accessors)
        {
            if(Resolve(accessor.Type, accessor.Key, accessor.IsOptional, container.Type.SortName, accessor.Name) is { } argument)
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
        ContainerPlan plan = new(container, plans.ToImmutableEquatableArray(), lookup.ToImmutableEquatableArray(), TopologicalOrder(plans), accessors.ToImmutableEquatableArray());
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
                case 0 when WrapperTypes.IsWrapper(type):
                    // Wrapper types and collections come in a later slice. The container must create them itself and
                    // must merge a collection with the fallback provider, so until then they stay missing, also when
                    // they are optional or the container has a fallback provider.
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
    }

    /// <summary>
    /// The full check of the dependency graph, on the edges of the resolved arguments: a built-in service, also this
    /// container class, is not a node. Each problem keeps its dependency site. The output does not report a problem
    /// that the definite check already reported with the same ID at the same site (Analysis.Model.spec, "Two levels of
    /// checks"), but the problem still stops the generation.
    /// </summary>
    private static void CheckGraph(ImmutableEquatableArray<ClosedDefinition> definitions, List<GraphEdge>[] edges, List<ContainerProblem> problems)
    {
        problems.AddRange(DependencyGraph.Cycles(definitions, edges)
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
    private static ImmutableEquatableArray<int> TopologicalOrder(List<DefinitionPlan> plans)
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
            }

            order.Add(index);
        }
    }
}
