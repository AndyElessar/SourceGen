using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Emit;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Analysis;

/// <summary>An edge of the dependency graph: dependency site <paramref name="Parameter"/> of <paramref name="From"/> resolves to <paramref name="To"/>.</summary>
/// <param name="From">The index of the closed definition that owns the dependency site.</param>
/// <param name="Parameter">The index of the dependency site in <see cref="ClosedDefinition.Dependencies"/>.</param>
/// <param name="To">The index of the closed definition that the dependency resolves to.</param>
/// <param name="Deferred">
/// The deferred wrapper of the edge. An edge through <c>Lazy</c> or <c>Func</c> is a deferred edge: it does not count
/// for a cycle, and a Singleton that holds a <c>Func</c> of a Transient does not hold the Transient.
/// </param>
/// <param name="Mapped">
/// For <c>Func&lt;A1, ..., An, T&gt;</c>: the constructor parameters of <c>T</c> that the caller supplies. The lifetime
/// checks do not follow the edges of these parameters through this edge.
/// </param>
internal readonly record struct GraphEdge(int From, int Parameter, int To, DeferredKind Deferred = DeferredKind.None, ImmutableEquatableArray<int>? Mapped = null);

/// <summary>A problem of the dependency graph. The definite check reports it at the dependency site.</summary>
/// <param name="Id">The diagnostic ID.</param>
/// <param name="Arguments">The message arguments.</param>
/// <param name="Source">The source that owns the dependency site.</param>
/// <param name="Site">The dependency site.</param>
internal sealed record GraphProblem(string Id, ImmutableEquatableArray<string> Arguments, DefinitionSource Source, SitePosition Site);

/// <summary>
/// The checks of the dependency graph (Analysis.Model.spec, "Circular dependencies", "Lifetime checks", "Warning for a
/// held Transient"). The nodes are the closed definitions. Built-in services are not nodes.
/// </summary>
/// <remarks>
/// Tags and modules come in later slices. Until then every single dependency with one candidate is definite
/// (Analysis.Model.spec, "Definite edges"). A single dependency with more than one candidate is <c>SGIOC401</c> and is
/// not an edge. A dependency on multiple services is never definite: only the full check has its edges, one for each
/// candidate.
/// </remarks>
internal static class DependencyGraph
{
    /// <summary>Gets the closed definitions that provide each service type with each key.</summary>
    /// <param name="definitions">The closed definitions.</param>
    /// <returns>
    /// The indexes of the providers, by service type and the canonical string of the key (the empty string for no key), in
    /// the ordering.
    /// </returns>
    public static Dictionary<(TypeRef Type, string Key), List<int>> Providers(ImmutableEquatableArray<ClosedDefinition> definitions)
    {
        Dictionary<(TypeRef, string), List<int>> providers = [];
        for(int i = 0; i < definitions.Length; i++)
        {
            string key = KeyRef.CanonicalOf(definitions[i].Key);
            foreach(TypeRef serviceType in definitions[i].ServiceTypes)
            {
                if(!providers.TryGetValue((serviceType, key), out List<int>? list))
                {
                    providers[(serviceType, key)] = list = [];
                }

                list.Add(i);
            }
        }

        return providers;
    }

    /// <summary>
    /// Gets the edges of the assembly view for each closed definition, in the order of its parameters. A container
    /// class cannot be registered, so it has no provider and is not a node (Analysis.Model.spec, "Built-in services").
    /// </summary>
    /// <param name="definitions">The closed definitions.</param>
    /// <param name="providers">The providers of each service type.</param>
    /// <returns>The edges, by the index of the closed definition that owns them.</returns>
    private static List<GraphEdge>[] Edges(ImmutableEquatableArray<ClosedDefinition> definitions, Dictionary<(TypeRef Type, string Key), List<int>> providers)
    {
        List<GraphEdge>[] edges = new List<GraphEdge>[definitions.Length];
        for(int i = 0; i < definitions.Length; i++)
        {
            edges[i] = [];
            ImmutableEquatableArray<Dependency> parameters = definitions[i].Dependencies;
            for(int p = 0; p < parameters.Length; p++)
            {
                // Only the Deferred shape is definite (Analysis.Model.spec, "Definite edges")
                if(parameters[p].KeyMode != SiteKeyMode.ServiceKey
                    && WrapperRequest.Parse(parameters[p].Type) is { Shape: RequestShape.Single, KeyValuePair: false } request
                    && providers.TryGetValue((request.Core, KeyRef.CanonicalOf(parameters[p].KeyIn(definitions[i]))), out List<int>? candidates)
                    && candidates.Count == 1)
                {
                    ImmutableEquatableArray<int>? mapped = request.IsSingleFuncWithArguments && request.Map(definitions[candidates[0]], out ImmutableEquatableArray<int> mapping) is null ? mapping : null;
                    edges[i].Add(new GraphEdge(i, p, candidates[0], request.Deferred, mapped));
                }
            }
        }

        return edges;
    }

    /// <summary>The definite check of the assembly view (Analysis.Model.spec, "Two levels of checks").</summary>
    /// <param name="definitions">The closed definitions of the assembly view.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The problems, each at its dependency site.</returns>
    public static ImmutableEquatableArray<GraphProblem> DefiniteCheck(ImmutableEquatableArray<ClosedDefinition> definitions, CancellationToken cancellationToken)
    {
        Dictionary<(TypeRef Type, string Key), List<int>> providers = Providers(definitions);
        List<GraphEdge>[] edges = Edges(definitions, providers);
        cancellationToken.ThrowIfCancellationRequested();
        return UnsupportedWrappers(definitions)
            .Concat(FuncMappings(definitions, providers))
            .Concat(Cycles(definitions, edges))
            .Concat(Captives(definitions, edges))
            .Concat(HeldTransients(definitions, edges))
            .ToImmutableEquatableArray();
    }

    /// <summary><c>SGIOC304</c>: each dependency site whose requested type the grammar of wrapper types does not allow.</summary>
    public static IEnumerable<GraphProblem> UnsupportedWrappers(ImmutableEquatableArray<ClosedDefinition> definitions)
    {
        for(int i = 0; i < definitions.Length; i++)
        {
            ImmutableEquatableArray<Dependency> dependencies = definitions[i].Dependencies;
            for(int p = 0; p < dependencies.Length; p++)
            {
                if(dependencies[p].KeyMode != SiteKeyMode.ServiceKey && WrapperRequest.Parse(dependencies[p].Type) is null)
                {
                    yield return Problem(Descriptors.UnsupportedWrapper.Id, [TypeNames.Display(dependencies[p].Type)], definitions, new GraphEdge(i, p, i));
                }
            }
        }
    }

    /// <summary>
    /// <c>SGIOC305</c> of the definite check (Analysis.Model.spec, "Two levels of checks"): two arguments of the same
    /// type at each site, and the lifetime and the mapping of the target on a definite edge. The full check of a
    /// container finds the same problems with the same message, so it does not report them again.
    /// </summary>
    private static IEnumerable<GraphProblem> FuncMappings(ImmutableEquatableArray<ClosedDefinition> definitions, Dictionary<(TypeRef Type, string Key), List<int>> providers)
    {
        for(int i = 0; i < definitions.Length; i++)
        {
            ImmutableEquatableArray<Dependency> dependencies = definitions[i].Dependencies;
            for(int p = 0; p < dependencies.Length; p++)
            {
                if(dependencies[p].KeyMode == SiteKeyMode.ServiceKey || WrapperRequest.Parse(dependencies[p].Type) is not { IsSingleFuncWithArguments: true } request)
                {
                    continue;
                }

                // Condition 4 does not depend on the candidates, so it comes first (Diagnostics.spec, SGIOC305)
                string? reason = request.DuplicateArgument();
                if(reason is null
                    && providers.TryGetValue((request.Core, KeyRef.CanonicalOf(dependencies[p].KeyIn(definitions[i]))), out List<int>? candidates)
                    && candidates.Count == 1)
                {
                    reason = request.Map(definitions[candidates[0]], out _);
                }

                if(reason is not null)
                {
                    yield return Problem(Descriptors.InvalidFuncWithArguments.Id, [TypeNames.Display(dependencies[p].Type), reason], definitions, new GraphEdge(i, p, i));
                }
            }
        }
    }

    /// <summary>
    /// <c>SGIOC301</c>: one cycle for each strongly connected component that has a cycle. The cycle starts at the first
    /// closed definition of the component in the ordering, and the generator reports it at the first edge. A deferred
    /// edge does not count.
    /// </summary>
    public static IEnumerable<GraphProblem> Cycles(ImmutableEquatableArray<ClosedDefinition> definitions, List<GraphEdge>[] allEdges)
    {
        List<GraphEdge>[] edges = [.. allEdges.Select(static e => e.Where(static edge => edge.Deferred == DeferredKind.None).ToList())];
        foreach(List<int> component in Components(edges))
        {
            int start = component.Min();
            HashSet<int> members = [.. component];
            if(ShortestCycle(start, members, edges) is not { } cycle)
            {
                continue;
            }

            IEnumerable<string> names = cycle.Select(e => KeyNames.Definition(definitions[e.From])).Append(KeyNames.Definition(definitions[start]));
            yield return Problem(Descriptors.CircularDependency.Id, [string.Join(" → ", names)], definitions, cycle[0]);
        }
    }

    /// <summary>
    /// <c>SGIOC302</c>: a Scoped closed definition that a Singleton reaches directly or through Transient closed
    /// definitions. The generator reports each pair one time, with the shortest path, at the first edge of the path. A
    /// closed definition that the walk enters through <c>Func&lt;A1, ..., An, T&gt;</c> does not follow the parameters
    /// that the caller supplies (Analysis.Model.spec, "Lifetime checks").
    /// </summary>
    public static IEnumerable<GraphProblem> Captives(ImmutableEquatableArray<ClosedDefinition> definitions, List<GraphEdge>[] edges)
    {
        for(int singleton = 0; singleton < definitions.Length; singleton++)
        {
            if(definitions[singleton].Lifetime != Lifetime.Singleton)
            {
                continue;
            }

            // A closed definition can be entered with and without supplied parameters, so the walk visits each pair, and
            // the path goes back through the pairs
            Dictionary<(int, ImmutableEquatableArray<int>?), (GraphEdge Edge, ImmutableEquatableArray<int>? FromSupplied)> previous = [];
            HashSet<int> reported = [];
            HashSet<(int, ImmutableEquatableArray<int>?)> visited = [(singleton, null)];
            Queue<(int Node, ImmutableEquatableArray<int>? Supplied)> queue = new([(singleton, null)]);
            while(queue.Count > 0)
            {
                (int node, ImmutableEquatableArray<int>? supplied) = queue.Dequeue();
                foreach(GraphEdge edge in edges[node].Where(e => supplied is null || !supplied.Contains(e.Parameter)))
                {
                    switch(definitions[edge.To].Lifetime)
                    {
                        case Lifetime.Scoped when reported.Add(edge.To):
                            List<GraphEdge> path = [edge];
                            for((int Node, ImmutableEquatableArray<int>? Supplied) at = (node, supplied); at.Node != singleton;)
                            {
                                (GraphEdge back, ImmutableEquatableArray<int>? fromSupplied) = previous[at];
                                path.Insert(0, back);
                                at = (back.From, fromSupplied);
                            }

                            string through = path.Count == 1
                                ? string.Empty
                                : " through " + string.Join(" → ", path.Select(e => KeyNames.Definition(definitions[e.From])).Append(KeyNames.Definition(definitions[edge.To])));
                            yield return Problem(
                                Descriptors.CaptiveDependency.Id,
                                [KeyNames.Definition(definitions[singleton]), KeyNames.Definition(definitions[edge.To]), through],
                                definitions,
                                path[0]);
                            break;
                        case Lifetime.Transient when edge.To != singleton && visited.Add((edge.To, edge.Mapped)):
                            previous[(edge.To, edge.Mapped)] = (edge, supplied);
                            queue.Enqueue((edge.To, edge.Mapped));
                            break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// <c>SGIOC502</c> and <c>SGIOC503</c>: a Singleton or Scoped closed definition that holds a Transient instance. A
    /// dependency on multiple services is reported one time, with its first Transient candidate. A <c>Func</c> creates a
    /// new instance for each call, so its holder holds only the factory.
    /// </summary>
    public static IEnumerable<GraphProblem> HeldTransients(ImmutableEquatableArray<ClosedDefinition> definitions, List<GraphEdge>[] edges)
    {
        HashSet<(int, int)> reported = [];
        foreach(GraphEdge edge in edges.SelectMany(static e => e).Where(static e => e.Deferred != DeferredKind.Func))
        {
            ClosedDefinition holder = definitions[edge.From];
            if(definitions[edge.To].Lifetime != Lifetime.Transient || holder.Lifetime == Lifetime.Transient || !reported.Add((edge.From, edge.Parameter)))
            {
                continue;
            }

            TypeRef requested = holder.Dependencies[edge.Parameter].Type;

            string id = holder.Lifetime == Lifetime.Singleton ? Descriptors.SingletonHoldsTransient.Id : Descriptors.ScopedHoldsTransient.Id;
            yield return Problem(
                id,
                [KeyNames.Definition(holder), KeyNames.Definition(definitions[edge.To]), TypeNames.Display(WrapperRequest.Parse(requested)?.Core ?? requested)],
                definitions,
                edge);
        }
    }

    private static GraphProblem Problem(string id, ImmutableEquatableArray<string> arguments, ImmutableEquatableArray<ClosedDefinition> definitions, GraphEdge site)
        => new(id, arguments, definitions[site.From].Source, definitions[site.From].SiteOf(site.Parameter));

    /// <summary>The edges from <paramref name="start"/> to <paramref name="node"/>, from the breadth-first walk.</summary>
    private static List<GraphEdge> PathTo(int node, int start, Dictionary<int, GraphEdge> previous)
    {
        List<GraphEdge> path = [];
        while(node != start)
        {
            GraphEdge edge = previous[node];
            path.Add(edge);
            node = edge.From;
        }

        path.Reverse();
        return path;
    }

    /// <summary>The shortest cycle through <paramref name="start"/> inside one component, or <see langword="null"/>.</summary>
    private static List<GraphEdge>? ShortestCycle(int start, HashSet<int> members, List<GraphEdge>[] edges)
    {
        Dictionary<int, GraphEdge> previous = [];
        Queue<int> queue = new([start]);
        while(queue.Count > 0)
        {
            foreach(GraphEdge edge in edges[queue.Dequeue()])
            {
                if(edge.To == start)
                {
                    return [.. PathTo(edge.From, start, previous), edge];
                }

                if(members.Contains(edge.To) && !previous.ContainsKey(edge.To))
                {
                    previous[edge.To] = edge;
                    queue.Enqueue(edge.To);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The strongly connected components (Tarjan), ordered by their first closed definition. The walk keeps its own
    /// stack, so a long dependency chain does not overflow the call stack.
    /// </summary>
    private static IEnumerable<List<int>> Components(List<GraphEdge>[] edges)
    {
        int count = edges.Length;
        int[] index = new int[count];
        int[] low = new int[count];
        bool[] onStack = new bool[count];
        Stack<int> stack = new();
        Stack<(int Node, int Edge)> walk = new();
        List<List<int>> components = [];
        int next = 1;
        for(int i = 0; i < count; i++)
        {
            if(index[i] == 0)
            {
                Enter(i);
            }

            while(walk.Count > 0)
            {
                (int node, int edge) = walk.Pop();
                if(edge < edges[node].Count)
                {
                    // Visit the next edge of the node, then come back to the node
                    walk.Push((node, edge + 1));
                    int to = edges[node][edge].To;
                    if(index[to] == 0)
                    {
                        Enter(to);
                    }
                    else if(onStack[to])
                    {
                        low[node] = Math.Min(low[node], index[to]);
                    }

                    continue;
                }

                // All edges of the node are done: give its low link to its caller
                if(walk.Count > 0)
                {
                    int caller = walk.Peek().Node;
                    low[caller] = Math.Min(low[caller], low[node]);
                }

                if(low[node] == index[node])
                {
                    List<int> component = [];
                    int member;
                    do
                    {
                        member = stack.Pop();
                        onStack[member] = false;
                        component.Add(member);
                    }
                    while(member != node);
                    components.Add(component);
                }
            }
        }

        return components.OrderBy(static c => c.Min());

        void Enter(int node)
        {
            index[node] = low[node] = next++;
            stack.Push(node);
            onStack[node] = true;
            walk.Push((node, 0));
        }
    }
}
