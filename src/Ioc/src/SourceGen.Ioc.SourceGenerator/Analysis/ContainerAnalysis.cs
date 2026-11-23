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
}

/// <summary>The resolution result of one constructor parameter.</summary>
/// <param name="Kind">How the argument is resolved.</param>
/// <param name="Target">The index of the target closed definition, for <see cref="ArgumentKind.Resolved"/>.</param>
internal sealed record ArgumentPlan(ArgumentKind Kind, int Target);

/// <summary>The construction plan of one closed definition (Analysis.Model.spec, "Construction plan").</summary>
/// <param name="Definition">The closed definition.</param>
/// <param name="Arguments">The arguments, in the order of the constructor parameters.</param>
internal sealed record DefinitionPlan(ClosedDefinition Definition, ImmutableEquatableArray<ArgumentPlan> Arguments);

/// <summary>An item of the lookup table (Container.Output.spec, "Contents of the table").</summary>
/// <param name="ServiceType">The service type <c>S</c>.</param>
/// <param name="Candidates">The indexes of the closed definitions that provide it, in their ordering.</param>
internal sealed record LookupEntry(TypeRef ServiceType, ImmutableEquatableArray<int> Candidates);

/// <summary>Everything that the code of a container needs.</summary>
/// <param name="Container">The container class.</param>
/// <param name="Definitions">The construction plans, in the ordering of the closed definitions.</param>
/// <param name="Lookup">The service types of the lookup table, by canonical string.</param>
/// <param name="TopologicalOrder">The indexes of the closed definitions, each dependency before the definitions that depend on it.</param>
internal sealed record ContainerPlan(
    ContainerModel Container,
    ImmutableEquatableArray<DefinitionPlan> Definitions,
    ImmutableEquatableArray<LookupEntry> Lookup,
    ImmutableEquatableArray<int> TopologicalOrder);

/// <summary>A problem that the full check finds. The position is added from the site of the container.</summary>
/// <param name="Id">The diagnostic ID.</param>
/// <param name="Arguments">The message arguments.</param>
internal sealed record ContainerProblem(string Id, ImmutableEquatableArray<string> Arguments);

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
    /// <param name="errorNodes">The implementation types and the known service types of the skipped instance definitions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The plan and the problems.</returns>
    public static ContainerAnalysisResult Analyze(
        ContainerModel container,
        ImmutableEquatableArray<ClosedDefinition> definitions,
        ImmutableEquatableArray<TypeRef> errorNodes,
        CancellationToken cancellationToken)
    {
        Dictionary<TypeRef, List<int>> providers = [];
        for(int i = 0; i < definitions.Length; i++)
        {
            foreach(TypeRef serviceType in definitions[i].ServiceTypes)
            {
                if(!providers.TryGetValue(serviceType, out List<int>? list))
                {
                    providers[serviceType] = list = [];
                }

                list.Add(i);
            }
        }

        HashSet<TypeRef> skipped = [.. errorNodes];
        List<ContainerProblem> problems = [];
        List<DefinitionPlan> plans = [];
        bool reachesErrorNode = false;
        foreach(ClosedDefinition definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<ArgumentPlan> arguments = [];
            foreach(ConstructorParameter parameter in definition.ConstructorParameters)
            {
                if(BuiltIn(parameter.Type, container.Type) is { } builtIn)
                {
                    arguments.Add(new ArgumentPlan(builtIn, -1));
                    continue;
                }

                switch(providers.TryGetValue(parameter.Type, out List<int>? candidates) ? candidates.Count : 0)
                {
                    case 1:
                        arguments.Add(new ArgumentPlan(ArgumentKind.Resolved, candidates![0]));
                        break;
                    case 0 when skipped.Contains(parameter.Type):
                        // An error node: the original error is already reported, and the build fails because of it
                        reachesErrorNode = true;
                        break;
                    case 0:
                        problems.Add(new ContainerProblem(Descriptors.MissingDependency.Id, [Site(definition, parameter), TypeNames.Display(parameter.Type)]));
                        break;
                    default:
                        problems.Add(new ContainerProblem(
                            Descriptors.AmbiguousDependency.Id,
                            [Site(definition, parameter), TypeNames.Display(parameter.Type), candidates!.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Join(", ", candidates.Select(c => definitions[c].Implementation.SortName))]));
                        break;
                }
            }

            plans.Add(new DefinitionPlan(definition, arguments.ToImmutableEquatableArray()));
        }

        if(problems.Count > 0 || reachesErrorNode)
        {
            return new(container.Type, null, problems.ToImmutableEquatableArray());
        }

        LookupEntry[] lookup = [.. providers
            .OrderBy(static p => p.Key.CanonicalString, StringComparer.Ordinal)
            .Select(static p => new LookupEntry(p.Key, p.Value.ToImmutableEquatableArray()))];
        ContainerPlan plan = new(container, plans.ToImmutableEquatableArray(), lookup.ToImmutableEquatableArray(), TopologicalOrder(plans));
        return new(container.Type, plan, []);
    }

    /// <summary>The built-in services (Analysis.Model.spec, "Built-in services").</summary>
    private static ArgumentKind? BuiltIn(TypeRef type, NamedTypeRef container)
    {
        if(type.Equals(container) || BuiltInServices.IsServiceProvider(type))
        {
            return ArgumentKind.Self;
        }

        return type is NamedTypeRef { Arguments: null } named && BuiltInServices.IsRootService(named.MetadataName) ? ArgumentKind.Root : null;
    }

    /// <summary>A dependency site in a message (Diagnostics.spec, "Names in messages").</summary>
    private static string Site(ClosedDefinition definition, ConstructorParameter parameter) => $"{definition.Implementation.SortName} → {parameter.Name}";

    /// <summary>
    /// Each dependency before the definitions that depend on it. Definitions without a relation follow the ordering.
    /// A cycle (SGIOC301, a later slice) cannot make the walk endless, because each definition is visited once.
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
