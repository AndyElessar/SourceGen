namespace SourceGen.Ioc;

/// <summary>
/// Partial class for ServiceKey and KeyValuePair analysis (SGIOC013-015).
/// </summary>
public sealed partial class RegisterAnalyzer
{
    /// <summary>
    /// SGIOC013 and SGIOC014: Analyzes [ServiceKey] parameter usage.
    /// SGIOC013: Reports error when the parameter type does not match the registered key type.
    /// SGIOC014: Reports error when [ServiceKey] is used but no Key is registered.
    /// </summary>
    private static void AnalyzeServiceKeyTypeMismatch(
        Action<Diagnostic> reportDiagnostic,
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken)
    {
        var keyTypeSymbol = serviceInfo.KeyTypeSymbol;
        var hasKey = serviceInfo.HasKey;

        // Check constructor parameters (using cached constructor)
        var constructor = serviceInfo.Constructor;
        if (constructor is not null)
        {
            AnalyzeServiceKeyParametersInMethod(reportDiagnostic, constructor.Parameters, keyTypeSymbol, hasKey, cancellationToken);
        }

        // Check [Inject] method parameters (using cached injected members)
        foreach (var (member, _) in serviceInfo.InjectedMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (member is not IMethodSymbol method)
                continue;

            AnalyzeServiceKeyParametersInMethod(reportDiagnostic, method.Parameters, keyTypeSymbol, hasKey, cancellationToken);
        }
    }

    /// <summary>
    /// Analyzes parameters for [ServiceKey] attribute and reports diagnostics.
    /// SGIOC013: Reports when parameter type does not match registered key type.
    /// SGIOC014: Reports when [ServiceKey] is used but no Key is registered.
    /// </summary>
    private static void AnalyzeServiceKeyParametersInMethod(
        Action<Diagnostic> reportDiagnostic,
        ImmutableArray<IParameterSymbol> parameters,
        ITypeSymbol? keyTypeSymbol,
        bool hasKey,
        CancellationToken cancellationToken)
    {
        foreach (var param in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if parameter has [ServiceKey] attribute
            var serviceKeyAttribute = param.GetAttributes()
                .FirstOrDefault(attr =>
                    attr.AttributeClass?.Name == "ServiceKeyAttribute"
                    && attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection");

            if (serviceKeyAttribute is null)
                continue;

            var location = serviceKeyAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                ?? param.Locations.FirstOrDefault();

            // SGIOC014: No key is registered but [ServiceKey] is used
            if (!hasKey)
            {
                reportDiagnostic(Diagnostic.Create(
                    ServiceKeyNotRegistered,
                    location,
                    param.Name));
                continue;
            }

            // Skip type checking when the key type cannot be resolved in collection phase (e.g., KeyType.Csharp)
            if (keyTypeSymbol is null)
                continue;

            var paramType = param.Type;

            // SGIOC013: Check if the parameter type is compatible with the key type
            // The parameter type should be the same as or assignable from the key type
            if (!IsAssignable(paramType, keyTypeSymbol))
            {
                reportDiagnostic(Diagnostic.Create(
                    ServiceKeyTypeMismatch,
                    location,
                    param.Name,
                    paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    keyTypeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }
    }

    /// <summary>
    /// SGIOC015: Analyzes KeyValuePair/Dictionary parameters for key type mismatches.
    /// Reports when a KeyValuePair&lt;K, V&gt; or Dictionary&lt;K, V&gt; is injected but no keyed service
    /// for V has a key type compatible with K.
    /// </summary>
    private static void AnalyzeKeyValuePairKeyTypeMismatch(
        Action<Diagnostic> reportDiagnostic,
        ServiceInfo serviceInfo,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        // Skip services with Factory or Instance — they handle their own resolution
        if (serviceInfo.HasFactory || serviceInfo.HasInstance)
            return;

        // Check constructor parameters
        var constructor = serviceInfo.Constructor;
        if (constructor is not null)
        {
            AnalyzeKvpParametersInMethod(reportDiagnostic, constructor.Parameters, analyzerContext, cancellationToken);
        }

        // Check [Inject] method parameters and property/field types
        foreach (var (member, _) in serviceInfo.InjectedMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (member)
            {
                case IMethodSymbol method:
                    AnalyzeKvpParametersInMethod(reportDiagnostic, method.Parameters, analyzerContext, cancellationToken);
                    break;
                case IPropertySymbol property:
                    AnalyzeKvpType(reportDiagnostic, property.Type, property.Name, property.Locations.FirstOrDefault(), analyzerContext, cancellationToken);
                    break;
                case IFieldSymbol field:
                    AnalyzeKvpType(reportDiagnostic, field.Type, field.Name, field.Locations.FirstOrDefault(), analyzerContext, cancellationToken);
                    break;
            }
        }
    }

    /// <summary>
    /// Analyzes method parameters for KeyValuePair/Dictionary key type mismatches.
    /// Skips parameters with [FromKeyedServices] attribute.
    /// </summary>
    private static void AnalyzeKvpParametersInMethod(
        Action<Diagnostic> reportDiagnostic,
        ImmutableArray<IParameterSymbol> parameters,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        foreach (var param in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip parameters with [FromKeyedServices] — those resolve specific keyed services
            if (param.GetAttributes().Any(static attr =>
                attr.AttributeClass?.Name == "FromKeyedServicesAttribute"
                && attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection"))
            {
                continue;
            }

            AnalyzeKvpType(reportDiagnostic, param.Type, param.Name, param.Locations.FirstOrDefault(), analyzerContext, cancellationToken);
        }
    }

    /// <summary>
    /// Analyzes a single type for KeyValuePair/Dictionary key type mismatches.
    /// Extracts K and V from the type and checks if any keyed service for V has a compatible key type.
    /// </summary>
    private static void AnalyzeKvpType(
        Action<Diagnostic> reportDiagnostic,
        ITypeSymbol type,
        string memberName,
        Location? location,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        if (!TryExtractKvpKeyAndValueTypes(type, out var keyType, out var valueType))
            return;

        // K = object is always compatible with any key type
        if (keyType.SpecialType is SpecialType.System_Object)
            return;

        // Find all keyed registrations for the value type V
        var hasAnyKeyedService = false;
        var hasCompatibleKeyType = false;

        foreach (var kvp in analyzerContext.RegisteredServices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidateInfo = kvp.Value;
            if (!candidateInfo.HasKey)
                continue;

            // Check if the candidate service type is assignable to V
            if (!IsAssignable(valueType, candidateInfo.Type))
                continue;

            hasAnyKeyedService = true;

            // KeyTypeSymbol is null for KeyType.Csharp in symbol collection phase.
            if (candidateInfo.KeyTypeSymbol is null)
            {
                // Check whether nameof() resolution captured a concrete key type.
                if (analyzerContext.ResolvedCsharpKeyTypes.TryGetValue(
                    (candidateInfo.Type, candidateInfo.Location), out var resolvedKeyType))
                {
                    if (IsAssignable(keyType, resolvedKeyType))
                    {
                        hasCompatibleKeyType = true;
                        break;
                    }

                    // nameof() resolved but incompatible; continue searching other registrations.
                    continue;
                }

                // String literal key (or unresolved nameof) cannot infer type, treat as compatible.
                hasCompatibleKeyType = true;
                break;
            }

            // Check if the registration's key type is assignable to K
            if (IsAssignable(keyType, candidateInfo.KeyTypeSymbol))
            {
                hasCompatibleKeyType = true;
                break;
            }
        }

        // Only report if there are keyed services for V but none have a compatible key type
        if (hasAnyKeyedService && !hasCompatibleKeyType)
        {
            reportDiagnostic(Diagnostic.Create(
                KeyValuePairKeyTypeMismatch,
                location,
                memberName,
                keyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                valueType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    /// <summary>
    /// Tries to extract key type K and value type V from a type that has key-value semantics.
    /// Supports: KeyValuePair&lt;K,V&gt;, IDictionary&lt;K,V&gt;, IReadOnlyDictionary&lt;K,V&gt;, Dictionary&lt;K,V&gt;,
    /// and collection types wrapping KeyValuePair&lt;K,V&gt; (IEnumerable, IReadOnlyCollection, ICollection, IReadOnlyList, IList, array).
    /// </summary>
    private static bool TryExtractKvpKeyAndValueTypes(
        ITypeSymbol type,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? keyType,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? valueType)
    {
        keyType = null;
        valueType = null;

        // Handle array type: KeyValuePair<K, V>[]
        if (type is IArrayTypeSymbol arrayType)
        {
            return TryExtractFromKeyValuePairType(arrayType.ElementType, out keyType, out valueType);
        }

        if (type is not INamedTypeSymbol namedType || !namedType.IsGenericType)
            return false;

        var originalDef = namedType.OriginalDefinition;
        var fullName = originalDef.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Direct KeyValuePair<K, V>
        if (fullName is "global::System.Collections.Generic.KeyValuePair<TKey, TValue>")
        {
            keyType = namedType.TypeArguments[0];
            valueType = namedType.TypeArguments[1];
            return true;
        }

        // Dictionary types: IDictionary<K, V>, IReadOnlyDictionary<K, V>, Dictionary<K, V>
        if (fullName is "global::System.Collections.Generic.IDictionary<TKey, TValue>"
            or "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
            or "global::System.Collections.Generic.Dictionary<TKey, TValue>")
        {
            keyType = namedType.TypeArguments[0];
            valueType = namedType.TypeArguments[1];
            return true;
        }

        // Collection types wrapping KeyValuePair<K, V>:
        // IEnumerable<KVP>, IReadOnlyCollection<KVP>, ICollection<KVP>, IReadOnlyList<KVP>, IList<KVP>
        if (namedType.TypeArguments.Length == 1
            && fullName is "global::System.Collections.Generic.IEnumerable<T>"
            or "global::System.Collections.Generic.IReadOnlyCollection<T>"
            or "global::System.Collections.Generic.ICollection<T>"
            or "global::System.Collections.Generic.IReadOnlyList<T>"
            or "global::System.Collections.Generic.IList<T>")
        {
            return TryExtractFromKeyValuePairType(namedType.TypeArguments[0], out keyType, out valueType);
        }

        return false;
    }

    /// <summary>
    /// Tries to extract K and V from a KeyValuePair&lt;K, V&gt; type.
    /// </summary>
    private static bool TryExtractFromKeyValuePairType(
        ITypeSymbol type,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? keyType,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? valueType)
    {
        keyType = null;
        valueType = null;

        if (type is not INamedTypeSymbol namedType || !namedType.IsGenericType)
            return false;

        var fullName = namedType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (fullName is not "global::System.Collections.Generic.KeyValuePair<TKey, TValue>")
            return false;

        keyType = namedType.TypeArguments[0];
        valueType = namedType.TypeArguments[1];
        return true;
    }
}
