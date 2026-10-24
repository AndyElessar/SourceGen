namespace SourceGen.Ioc;

/// <summary>
/// Partial class for dependency analysis (SGIOC002-005: circular dependencies and lifetime conflicts).
/// </summary>
public sealed partial class RegisterAnalyzer
{
    private static void AnalyzeDependencies(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        ServiceInfo serviceInfo,
        HashSet<INamedTypeSymbol> visited,
        Stack<INamedTypeSymbol> pathStack,
        CancellationToken cancellationToken)
    {
        // Use cached constructor from ServiceInfo
        var constructor = serviceInfo.Constructor;
        if (constructor is null)
            return;

        var targetType = serviceInfo.Type;
        var currentLifetime = serviceInfo.Lifetime;
        var location = serviceInfo.Location;

        foreach (var parameter in constructor.Parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (parameter.Type is not INamedTypeSymbol parameterType)
                continue;

            // Find the dependency's implementation type and lifetime using index.
            // If direct lookup fails, try unwrapping Func<...>/Lazy<T> wrapper types.
            var dependencyInfo = FindRegisteredDependency(analyzerContext, parameterType)
                ?? (TryUnwrapServiceType(parameterType, out var unwrappedType)
                    ? FindRegisteredDependency(analyzerContext, unwrappedType)
                    : null);
            if (dependencyInfo is null)
                continue;

            // SGIOC002: Check for circular dependency (reuse visited set and path stack)
            visited.Clear();
            pathStack.Clear();
            if (DetectCircularDependency(analyzerContext, targetType, dependencyInfo.Type, visited, pathStack))
            {
                // Build cycle string from stack (already in correct order: current -> ... -> start)
                var cycleString = BuildCycleString(pathStack);
                reportDiagnostic(Diagnostic.Create(
                    CircularDependency,
                    location,
                    cycleString));
            }

            // SGIOC003-005: Check for lifetime conflict
            var lifetimeConflictDescriptor = GetLifetimeConflictDescriptor(currentLifetime, dependencyInfo.Lifetime);
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
        AnalyzerContext analyzerContext,
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

        // Check if current type is registered
        if (!analyzerContext.RegisteredServices.ContainsKey(currentType))
            return false;

        var constructor = currentType.SpecifiedOrPrimaryOrMostParametersConstructor;
        if (constructor is null)
            return false;

        foreach (var parameter in constructor.Parameters)
        {
            if (parameter.Type is not INamedTypeSymbol parameterType)
                continue;

            // Unwrap Func<...>/Lazy<T> wrapper types for circular dependency detection
            var dependency = FindRegisteredDependency(analyzerContext, parameterType)
                ?? (TryUnwrapServiceType(parameterType, out var unwrappedType)
                    ? FindRegisteredDependency(analyzerContext, unwrappedType)
                    : null);
            if (dependency is null)
                continue;

            if (DetectCircularDependency(analyzerContext, startType, dependency.Type, visited, pathStack))
            {
                pathStack.Push(currentType);
                return true;
            }
        }

        return false;
    }

    private static DiagnosticDescriptor? GetLifetimeConflictDescriptor(ServiceLifetime consumerLifetime, ServiceLifetime dependencyLifetime)
    {
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
    private static bool TryUnwrapServiceType(INamedTypeSymbol parameterType, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out INamedTypeSymbol? serviceType)
    {
        serviceType = null;

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
            serviceType = parameterType.TypeArguments[^1] as INamedTypeSymbol;
            return serviceType is not null;
        }

        return false;
    }
}
