namespace SourceGen.Ioc;

/// <summary>
/// Partial class for dependency analysis (SGIOC002-005: circular dependencies and lifetime conflicts).
/// </summary>
public sealed partial class RegisterAnalyzer
{
    private static void AnalyzeDependencies(
        Action<Diagnostic> reportDiagnostic,
        DependencyGraph graph,
        ServiceInfo serviceInfo,
        HashSet<INamedTypeSymbol> visited,
        Stack<INamedTypeSymbol> pathStack,
        CancellationToken cancellationToken)
    {
        var targetType = serviceInfo.Type;
        var currentLifetime = serviceInfo.Lifetime;
        var location = serviceInfo.Location;

        foreach (var dependencyInfo in graph.GetDependencies(targetType))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // SGIOC002: A cycle through this edge exists only when both ends share a strongly connected component.
            // The DFS still runs for those edges so the reported cycle path is unchanged.
            if (graph.IsInSameComponent(targetType, dependencyInfo.Type, cancellationToken))
            {
                visited.Clear();
                pathStack.Clear();
                if (DetectCircularDependency(graph, targetType, dependencyInfo.Type, visited, pathStack))
                {
                    // Build cycle string from stack (already in correct order: current -> ... -> start)
                    var cycleString = BuildCycleString(pathStack);
                    reportDiagnostic(Diagnostic.Create(
                        CircularDependency,
                        location,
                        cycleString));
                }
            }

            // SGIOC003-005: Check for lifetime conflict
            var lifetimeConflictDescriptor = GetLifetimeConflictDescriptor(currentLifetime, dependencyInfo.Lifetime, dependencyInfo.IsThroughFunc);
            if (lifetimeConflictDescriptor is not null)
            {
                reportDiagnostic(Diagnostic.Create(
                    lifetimeConflictDescriptor,
                    location,
                    targetType.Name,
                    dependencyInfo.Type.Name));
            }
        }
    }

    /// <summary>
    /// A registered constructor dependency of a service.
    /// </summary>
    /// <param name="Info">The registered dependency.</param>
    /// <param name="IsThroughFunc">
    /// Whether the dependency is resolved through a <c>Func&lt;...&gt;</c> factory, which resolves it on every call,
    /// so a transient dependency is not captured.
    /// </param>
    private readonly record struct DependencyEdge(ServiceInfo Info, bool IsThroughFunc)
    {
        public INamedTypeSymbol Type => Info.Type;

        public ServiceLifetime Lifetime => Info.Lifetime;
    }

    /// <summary>
    /// Registered service dependency graph with cached constructor dependencies and lazily computed
    /// strongly connected components (Tarjan), so edges that cannot form a cycle skip the cycle DFS.
    /// </summary>
    private sealed class DependencyGraph(AnalyzerContext analyzerContext)
    {
        private readonly Dictionary<INamedTypeSymbol, ImmutableArray<DependencyEdge>> _dependencies = new(SymbolEqualityComparer.Default);
        private Dictionary<INamedTypeSymbol, int>? _componentIds;

        /// <summary>
        /// Gets the registered dependencies of a registered service's constructor, in parameter order.
        /// Func&lt;...&gt;/Lazy&lt;T&gt; parameters are unwrapped when the direct lookup fails.
        /// </summary>
        public ImmutableArray<DependencyEdge> GetDependencies(INamedTypeSymbol type)
        {
            if (_dependencies.TryGetValue(type, out var cached))
                return cached;

            var constructor = analyzerContext.RegisteredServices.TryGetValue(type, out var info) ? info.Constructor : null;
            if (constructor is null)
            {
                _dependencies[type] = [];
                return [];
            }

            var builder = ImmutableArray.CreateBuilder<DependencyEdge>(constructor.Parameters.Length);
            foreach (var parameter in constructor.Parameters)
            {
                if (parameter.Type is not INamedTypeSymbol parameterType)
                    continue;

                if (FindRegisteredDependency(analyzerContext, parameterType) is { } dependency)
                {
                    builder.Add(new DependencyEdge(dependency, IsThroughFunc: false));
                }
                else if (TryUnwrapServiceType(parameterType, out var unwrappedType, out var isFunc)
                    && FindRegisteredDependency(analyzerContext, unwrappedType) is { } unwrappedDependency)
                {
                    builder.Add(new DependencyEdge(unwrappedDependency, isFunc));
                }
            }

            var result = builder.ToImmutable();
            _dependencies[type] = result;
            return result;
        }

        public bool IsInSameComponent(INamedTypeSymbol from, INamedTypeSymbol to, CancellationToken cancellationToken)
        {
            var componentIds = _componentIds ??= ComputeComponentIds(cancellationToken);
            return componentIds.TryGetValue(from, out var fromId)
                && componentIds.TryGetValue(to, out var toId)
                && fromId == toId;
        }

        private Dictionary<INamedTypeSymbol, int> ComputeComponentIds(CancellationToken cancellationToken)
        {
            var comparer = SymbolEqualityComparer.Default;
            var indices = new Dictionary<INamedTypeSymbol, int>(comparer);
            var lowLinks = new Dictionary<INamedTypeSymbol, int>(comparer);
            var onStack = new HashSet<INamedTypeSymbol>(comparer);
            var stack = new Stack<INamedTypeSymbol>();
            var componentIds = new Dictionary<INamedTypeSymbol, int>(comparer);
            var nextIndex = 0;
            var nextComponentId = 0;

            foreach (var type in analyzerContext.RegisteredServices.Keys)
            {
                if (!indices.ContainsKey(type))
                    StrongConnect(type);
            }

            return componentIds;

            void StrongConnect(INamedTypeSymbol node)
            {
                cancellationToken.ThrowIfCancellationRequested();

                indices[node] = nextIndex;
                lowLinks[node] = nextIndex;
                nextIndex++;
                stack.Push(node);
                onStack.Add(node);

                foreach (var dependency in GetDependencies(node))
                {
                    var next = dependency.Type;
                    if (!indices.TryGetValue(next, out var nextNodeIndex))
                    {
                        StrongConnect(next);
                        lowLinks[node] = Math.Min(lowLinks[node], lowLinks[next]);
                    }
                    else if (onStack.Contains(next))
                    {
                        lowLinks[node] = Math.Min(lowLinks[node], nextNodeIndex);
                    }
                }

                if (lowLinks[node] != indices[node])
                    return;

                INamedTypeSymbol member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    componentIds[member] = nextComponentId;
                }
                while (!comparer.Equals(member, node));

                nextComponentId++;
            }
        }
    }

    /// <summary>
    /// Builds the cycle string from the path stack without allocating intermediate collections.
    /// </summary>
    private static string BuildCycleString(Stack<INamedTypeSymbol> pathStack)
    {
        // Stack is LIFO, so we need to reverse for correct order
        StringBuilder sb = new(pathStack.Count * 2 - 1);
        foreach (var (i, item) in pathStack.Reverse().Index())
        {
            if (i > 0)
                sb.Append(" -> ");
            sb.Append(item.Name);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Finds a registered dependency by parameter type using O(1) index lookups.
    /// This method is called during CompilationEnd when all services are fully indexed.
    /// </summary>
    private static ServiceInfo? FindRegisteredDependency(
        AnalyzerContext analyzerContext,
        INamedTypeSymbol parameterType)
    {
        // Direct match - O(1)
        if (analyzerContext.RegisteredServices.TryGetValue(parameterType, out var directMatch))
            return directMatch;

        // Try open generic match for direct type (e.g., TestOpenGeneric2<int> -> TestOpenGeneric2<T>)
        if (parameterType.IsGenericType && !parameterType.IsUnboundGenericType)
        {
            var originalDefinition = parameterType.OriginalDefinition;
            if (analyzerContext.RegisteredServices.TryGetValue(originalDefinition, out var genericMatch))
                return genericMatch;
        }

        // Use index for interface/base class lookup - O(1)
        if (analyzerContext.ServiceTypeIndex.TryGetValue(parameterType, out var indexedMatch))
            return indexedMatch;

        // Try open generic match for indexed lookup
        if (parameterType.IsGenericType && !parameterType.IsUnboundGenericType)
        {
            var originalDefinition = parameterType.OriginalDefinition;
            if (analyzerContext.ServiceTypeIndex.TryGetValue(originalDefinition, out var genericIndexMatch))
                return genericIndexMatch;
        }

        // No fallback needed - this runs during CompilationEnd when index is complete
        return null;
    }

    /// <summary>
    /// Detects circular dependencies using a stack-based approach to avoid List.Insert(0,...) allocations.
    /// Returns true if a cycle is detected, with the path stored in pathStack.
    /// </summary>
    private static bool DetectCircularDependency(
        DependencyGraph graph,
        INamedTypeSymbol startType,
        INamedTypeSymbol currentType,
        HashSet<INamedTypeSymbol> visited,
        Stack<INamedTypeSymbol> pathStack)
    {
        if (SymbolEqualityComparer.Default.Equals(startType, currentType))
        {
            pathStack.Push(startType);
            return true;
        }

        if (!visited.Add(currentType))
            return false;

        foreach (var dependency in graph.GetDependencies(currentType))
        {
            if (DetectCircularDependency(graph, startType, dependency.Type, visited, pathStack))
            {
                pathStack.Push(currentType);
                return true;
            }
        }

        return false;
    }

    private static DiagnosticDescriptor? GetLifetimeConflictDescriptor(ServiceLifetime consumerLifetime, ServiceLifetime dependencyLifetime, bool isThroughFunc)
    {
        // A Func<...> factory resolves a new transient instance on every call, so nothing is captured (SGIOC004/005).
        // A scoped service resolved through it from a singleton still comes from the root (SGIOC003).
        if (isThroughFunc && dependencyLifetime is ServiceLifetime.Transient)
            return null;

        // SGIOC003: Singleton depending on Scoped is a conflict (captive dependency)
        if (consumerLifetime is ServiceLifetime.Singleton && dependencyLifetime is ServiceLifetime.Scoped)
            return SingletonDependsOnScoped;

        // SGIOC004: Singleton depending on Transient is a captive dependency issue
        // The Transient instance will be captured and live for the application lifetime
        if (consumerLifetime is ServiceLifetime.Singleton && dependencyLifetime is ServiceLifetime.Transient)
            return SingletonDependsOnTransient;

        // SGIOC005: Scoped depending on Transient is a captive dependency issue
        // The Transient instance will be captured for the scope lifetime
        if (consumerLifetime is ServiceLifetime.Scoped && dependencyLifetime is ServiceLifetime.Transient)
            return ScopedDependsOnTransient;

        return null;
    }

    /// <summary>
    /// Unwraps <c>Func&lt;...&gt;</c> and <c>Lazy&lt;T&gt;</c> wrapper types to extract the inner service type.
    /// For <c>Lazy&lt;T&gt;</c> and <c>Func&lt;T&gt;</c>, returns the single type argument.
    /// For <c>Func&lt;T1, ..., TReturn&gt;</c>, returns the last type argument (the return type).
    /// </summary>
    private static bool TryUnwrapServiceType(INamedTypeSymbol parameterType, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out INamedTypeSymbol? serviceType, out bool isFunc)
    {
        serviceType = null;
        isFunc = false;

        if (!parameterType.IsGenericType)
            return false;

        var originalDefinition = parameterType.OriginalDefinition;
        var containingNamespace = originalDefinition.ContainingNamespace;

        // Must be in the System namespace
        if (containingNamespace is not { Name: "System", ContainingNamespace.IsGlobalNamespace: true })
            return false;

        var name = originalDefinition.MetadataName;

        // Lazy<T> — arity 1, extract T
        if (name is "Lazy`1")
        {
            serviceType = parameterType.TypeArguments[0] as INamedTypeSymbol;
            return serviceType is not null;
        }

        // Func<...> — arity >= 1, extract last type argument (the return type)
        if (name.StartsWith("Func`", StringComparison.Ordinal))
        {
            isFunc = true;
            serviceType = parameterType.TypeArguments[^1] as INamedTypeSymbol;
            return serviceType is not null;
        }

        return false;
    }
}
