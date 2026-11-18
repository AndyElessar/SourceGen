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
        var dependencies = graph.GetDependencies(targetType);
        if (dependencies.IsEmpty)
            return;

        // The dependencies are those of the constructor, which only the constructed registrations call. A cycle is a
        // property of the implementation type, so it is reported once, at its first constructed registration.
        var cycleLocation = serviceInfo.GetFirstActivationLocation(injected: false);
        var activations = serviceInfo.Activations;

        for (var e = 0; e < dependencies.Length; e++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A parameter resolves to one registration per tag group; registrations of the same implementation type are
            // checked once for a cycle, and once per lifetime for a lifetime conflict
            var dependencyInfo = dependencies[e];

            // SGIOC002: A cycle through this edge exists only when both ends share a strongly connected component, and only
            // through a registration the container constructs. The DFS still runs for those edges so the reported cycle path
            // is unchanged.
            if (dependencyInfo.IsConstructed
                && !HasEarlierEdge(dependencies, e, sameLifetime: false)
                && graph.IsInSameComponent(targetType, dependencyInfo.Type, cancellationToken))
            {
                visited.Clear();
                pathStack.Clear();
                if (DetectCircularDependency(graph, targetType, dependencyInfo.Type, visited, pathStack))
                {
                    // Build cycle string from stack (already in correct order: current -> ... -> start)
                    var cycleString = BuildCycleString(pathStack);
                    reportDiagnostic(Diagnostic.Create(
                        CircularDependency,
                        cycleLocation,
                        cycleString));
                }
            }

            if (HasEarlierEdge(dependencies, e, sameLifetime: true))
                continue;

            // SGIOC003-005: Check every constructed registration with its own lifetime, at its own location
            for (var i = 0; i < activations.Length; i++)
            {
                var activation = activations[i];
                if (!activation.IsConstructed || IsReportedAtEarlierActivation(activations, i))
                    continue;

                var lifetimeConflictDescriptor = GetLifetimeConflictDescriptor(activation.Lifetime, dependencyInfo.Lifetime, dependencyInfo.IsThroughFunc);
                if (lifetimeConflictDescriptor is not null)
                {
                    reportDiagnostic(Diagnostic.Create(
                        lifetimeConflictDescriptor,
                        activation.Location,
                        targetType.Name,
                        dependencyInfo.Type.Name));
                }
            }
        }

        // Registrations without a source location share a null location: report a lifetime conflict there once
        static bool IsReportedAtEarlierActivation(ImmutableArray<ServiceActivation> activations, int index)
        {
            var activation = activations[index];
            for (var i = 0; i < index; i++)
            {
                var earlier = activations[i];
                if (earlier.IsConstructed && earlier.Lifetime == activation.Lifetime && Equals(earlier.Location, activation.Location))
                    return true;
            }

            return false;
        }

        // Whether an earlier edge of the same parameter resolves to the same implementation type (and lifetime), so this
        // edge reports nothing new
        static bool HasEarlierEdge(ImmutableArray<DependencyEdge> dependencies, int index, bool sameLifetime)
        {
            var edge = dependencies[index];
            for (var i = 0; i < index; i++)
            {
                var earlier = dependencies[i];
                if (earlier.ParameterIndex == edge.ParameterIndex
                    && ReferenceEquals(earlier.Dependency.Info, edge.Dependency.Info)
                    && (sameLifetime ? earlier.Lifetime == edge.Lifetime : earlier.IsConstructed))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// A registration a constructor parameter of a service resolves to.
    /// </summary>
    /// <param name="Dependency">The registration the parameter resolves to (see <see cref="DependencyIndex"/>).</param>
    /// <param name="IsThroughFunc">
    /// Whether the dependency is resolved through a <c>Func&lt;...&gt;</c> factory, which resolves it on every call,
    /// so a transient dependency is not captured.
    /// </param>
    /// <param name="ParameterIndex">The index of the constructor parameter.</param>
    private readonly record struct DependencyEdge(RegisteredDependency Dependency, bool IsThroughFunc, int ParameterIndex)
    {
        public INamedTypeSymbol Type => Dependency.Type;

        public ServiceLifetime Lifetime => Dependency.Lifetime;

        /// <summary>Gets whether the container calls the dependency's constructor, through which a cycle can continue.</summary>
        public bool IsConstructed => Dependency.Activation.IsConstructed;
    }

    /// <summary>
    /// Registered service dependency graph with cached constructor dependencies and lazily computed
    /// strongly connected components (Tarjan), so edges that cannot form a cycle skip the cycle DFS.
    /// </summary>
    private sealed class DependencyGraph(AnalyzerContext analyzerContext, DependencyIndex dependencyIndex)
    {
        private readonly Dictionary<INamedTypeSymbol, ImmutableArray<DependencyEdge>> _dependencies = new(SymbolEqualityComparer.Default);
        private Dictionary<INamedTypeSymbol, int>? _componentIds;

        /// <summary>
        /// Gets the registrations the constructor parameters of a registered service resolve to, in parameter order, matched
        /// by the parameter's service key. Func&lt;...&gt;/Lazy&lt;T&gt; parameters are unwrapped when the direct lookup fails.
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
            for (var i = 0; i < constructor.Parameters.Length; i++)
            {
                var parameter = constructor.Parameters[i];
                if (parameter.Type is not INamedTypeSymbol parameterType)
                    continue;

                // A [ServiceKey] parameter receives the registration's key, not a service
                var (key, _, hasServiceKey, _, _) = parameter.GetServiceKeyAndAttributeInfo();
                if (hasServiceKey)
                    continue;

                var isFunc = false;
                var dependencies = dependencyIndex.Find(parameterType, key);
                if (dependencies.IsEmpty && TryUnwrapServiceType(parameterType, out var unwrappedType, out isFunc))
                    dependencies = dependencyIndex.Find(unwrappedType, key);

                foreach (var dependency in dependencies)
                    builder.Add(new DependencyEdge(dependency, isFunc, i));
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
                    // A registration that is not constructed does not call the constructor, so no cycle continues through it
                    if (!dependency.IsConstructed)
                        continue;

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
            if (dependency.IsConstructed && DetectCircularDependency(graph, startType, dependency.Type, visited, pathStack))
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
