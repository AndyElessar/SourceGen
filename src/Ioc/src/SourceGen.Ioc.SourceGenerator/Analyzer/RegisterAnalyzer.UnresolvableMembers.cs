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
    /// <param name="constructedByFunc">
    /// The registrations a requested <c>Func</c> with input parameters constructs, also when they have an Instance or a
    /// Factory, with the <c>Func</c> types that construct them (see <see cref="CollectRegistrationsConstructedByFunc"/>).
    /// </param>
    private static void AnalyzeServiceKeyTypeMismatch(
        Action<Diagnostic> reportDiagnostic,
        ServiceInfo serviceInfo,
        Dictionary<ServiceActivation, List<INamedTypeSymbol>> constructedByFunc,
        IAssemblySymbol assembly,
        CancellationToken cancellationToken)
    {
        // Check constructor parameters (using cached constructor) against the registrations that call the constructor: the
        // constructed ones, and those a Func with input parameters constructs, which passes their key too unless it passes
        // one of its arguments
        var activations = serviceInfo.Activations;
        var funcConstructed = constructedByFunc.Count > 0 && activations.Any(constructedByFunc.ContainsKey);
        var constructor = serviceInfo.Constructor
            ?? (funcConstructed ? serviceInfo.Type.GetSelectedConstructor(assembly) : null);

        // The members of a type with only Instance or Factory registrations are not cached
        ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> injectedMembers = serviceInfo.InjectedMembers.IsEmpty && funcConstructed
            ? [.. serviceInfo.Type.GetInjectedMembers()]
            : serviceInfo.InjectedMembers;
        var funcContext = funcConstructed ? new FuncConstruction(constructedByFunc, constructor, injectedMembers) : null;

        if (constructor is not null)
        {
            AnalyzeServiceKeyParametersInMethod(reportDiagnostic, constructor.Parameters, activations, injected: false, cancellationToken, funcContext);
        }

        // Check [Inject] method parameters (using cached injected members) against the registrations that inject them, and
        // those that do not return Task (whatever the features) against those a Func with input parameters constructs, which
        // calls them with their key too
        foreach (var (member, _) in injectedMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (member is not IMethodSymbol method)
                continue;

            AnalyzeServiceKeyParametersInMethod(reportDiagnostic, method.Parameters, activations, injected: true, cancellationToken, AnalyzerHelpers.IsNonGenericTaskType(method.ReturnType) ? null : funcContext);
        }
    }

    /// <summary>
    /// The registrations of a type that requested <c>Func</c>s with input parameters construct, with what the <c>Func</c>s
    /// call: the type's selected constructor and its injected members.
    /// </summary>
    private sealed record FuncConstruction(
        Dictionary<ServiceActivation, List<INamedTypeSymbol>> ConstructedByFunc,
        IMethodSymbol? Constructor,
        ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> InjectedMembers)
    {
        /// <summary>
        /// Gets whether a <c>Func</c> constructing <paramref name="activation"/> passes the registration's key to
        /// <paramref name="parameter"/>: one that does not pass one of its arguments to it instead.
        /// </summary>
        public bool PassesKey(ServiceActivation activation, IParameterSymbol parameter)
            => ConstructedByFunc.TryGetValue(activation, out var funcTypes)
                && IsResolvedByAnyFunc(funcTypes, parameter, Constructor, InjectedMembers);
    }

    /// <summary>
    /// Gets whether one of <paramref name="funcTypes"/> resolves <paramref name="target"/> (a parameter, or an injected property
    /// or field) when it constructs <paramref name="constructor"/>'s type, instead of passing one of its arguments to it (see
    /// <see cref="IsPassedFuncArgument"/>).
    /// </summary>
    private static bool IsResolvedByAnyFunc(
        List<INamedTypeSymbol> funcTypes,
        ISymbol target,
        IMethodSymbol? constructor,
        ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> injectedMembers)
    {
        foreach (var funcType in funcTypes)
        {
            if (!IsPassedFuncArgument(funcType, target, constructor, injectedMembers))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Analyzes parameters for [ServiceKey] attribute and reports diagnostics, for each registration that passes its key to
    /// the parameter: those that construct (<paramref name="injected"/> false) or inject (<paramref name="injected"/> true)
    /// the instance, and those a <c>Func</c> in <paramref name="funcConstruction"/> constructs without passing one of its
    /// arguments to the parameter.
    /// SGIOC013: Reports when parameter type does not match registered key type, once per key type.
    /// SGIOC014: Reports when [ServiceKey] is used but no Key is registered, once per parameter.
    /// </summary>
    private static void AnalyzeServiceKeyParametersInMethod(
        Action<Diagnostic> reportDiagnostic,
        ImmutableArray<IParameterSymbol> parameters,
        ImmutableArray<ServiceActivation> activations,
        bool injected,
        CancellationToken cancellationToken,
        FuncConstruction? funcConstruction = null)
    {
        foreach (var param in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if parameter has [ServiceKey] attribute
            var serviceKeyAttribute = param.GetAttributes()
                .FirstOrDefault(static attr => AnalyzerHelpers.IsServiceKeyAttribute(attr.AttributeClass));

            if (serviceKeyAttribute is null)
                continue;

            var location = serviceKeyAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                ?? param.Locations.FirstOrDefault();

            // The diagnostics are reported at the parameter, so each problem is reported once for all the registrations
            var reportedNoKey = false;
            List<ITypeSymbol>? reportedKeyTypes = null;
            foreach (var activation in activations)
            {
                if (!((injected ? activation.IsInjected : activation.IsConstructed) || funcConstruction?.PassesKey(activation, param) == true))
                    continue;

                // SGIOC014: No key is registered but [ServiceKey] is used
                if (!activation.HasKey)
                {
                    if (!reportedNoKey)
                    {
                        reportedNoKey = true;
                        reportDiagnostic(Diagnostic.Create(
                            ServiceKeyNotRegisteredForRegistration,
                            location,
                            param.Name));
                    }

                    continue;
                }

                // Skip type checking when the key type cannot be resolved in collection phase (e.g., KeyType.Csharp)
                var keyTypeSymbol = activation.KeyTypeSymbol;
                if (keyTypeSymbol is null)
                    continue;

                var paramType = param.Type;

                // SGIOC013: Check if the parameter type is compatible with the key type
                // The parameter type should be the same as or assignable from the key type
                if (IsAssignable(paramType, keyTypeSymbol)
                    || reportedKeyTypes?.Contains(keyTypeSymbol, SymbolEqualityComparer.Default) == true)
                {
                    continue;
                }

                (reportedKeyTypes ??= []).Add(keyTypeSymbol);
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
        KeyedServiceIndex keyedServiceIndex,
        CancellationToken cancellationToken)
    {
        // Skip services whose registrations all have Factory or Instance — they handle their own resolution
        if (!serviceInfo.HasConstructorActivation)
            return;

        // Check constructor parameters
        var constructor = serviceInfo.Constructor;
        if (constructor is not null)
        {
            AnalyzeKvpParametersInMethod(reportDiagnostic, constructor.Parameters, analyzerContext, keyedServiceIndex, cancellationToken);
        }

        // Check [Inject] method parameters and property/field types
        foreach (var (member, _) in serviceInfo.InjectedMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (member)
            {
                case IMethodSymbol method:
                    AnalyzeKvpParametersInMethod(reportDiagnostic, method.Parameters, analyzerContext, keyedServiceIndex, cancellationToken);
                    break;
                case IPropertySymbol property:
                    AnalyzeKvpType(reportDiagnostic, property.Type, property.Name, property.Locations.FirstOrDefault(), analyzerContext, keyedServiceIndex, cancellationToken);
                    break;
                case IFieldSymbol field:
                    AnalyzeKvpType(reportDiagnostic, field.Type, field.Name, field.Locations.FirstOrDefault(), analyzerContext, keyedServiceIndex, cancellationToken);
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
        KeyedServiceIndex keyedServiceIndex,
        CancellationToken cancellationToken)
    {
        foreach (var param in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip parameters with [FromKeyedServices] — those resolve specific keyed services
            if (param.GetAttributes().Any(static attr => AnalyzerHelpers.IsFromKeyedServicesAttribute(attr.AttributeClass)))
            {
                continue;
            }

            AnalyzeKvpType(reportDiagnostic, param.Type, param.Name, param.Locations.FirstOrDefault(), analyzerContext, keyedServiceIndex, cancellationToken);
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
        KeyedServiceIndex keyedServiceIndex,
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

        foreach (var candidateInfo in keyedServiceIndex.GetCandidates(valueType, cancellationToken))
        {
            hasAnyKeyedService = true;

            // KeyTypeSymbol is null for KeyType.Csharp in symbol collection phase.
            if (candidateInfo.KeyTypeSymbol is null)
            {
                // Check whether nameof() resolution captured a concrete key type.
                if (analyzerContext.ResolvedCsharpKeyTypes.TryGetValue(
                    (candidateInfo.Type, candidateInfo.Activation.Location), out var resolvedKeyType))
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

        // Collection types wrapping KeyValuePair<K, V>:
        // IEnumerable<KVP>, IReadOnlyCollection<KVP>, ICollection<KVP>, IReadOnlyList<KVP>, IList<KVP>
        if (originalDef.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T
            or SpecialType.System_Collections_Generic_IReadOnlyCollection_T
            or SpecialType.System_Collections_Generic_ICollection_T
            or SpecialType.System_Collections_Generic_IReadOnlyList_T
            or SpecialType.System_Collections_Generic_IList_T)
        {
            return TryExtractFromKeyValuePairType(namedType.TypeArguments[0], out keyType, out valueType);
        }

        // Direct KeyValuePair<K, V>
        // Dictionary types: IDictionary<K, V>, IReadOnlyDictionary<K, V>, Dictionary<K, V>
        if (originalDef.MetadataName is not ("KeyValuePair`2" or "IDictionary`2" or "IReadOnlyDictionary`2" or "Dictionary`2")
            || !IsCollectionsGenericTopLevelType(originalDef))
        {
            return false;
        }

        keyType = namedType.TypeArguments[0];
        valueType = namedType.TypeArguments[1];
        return true;
    }

    /// <summary>
    /// Checks that a type is declared directly in <c>System.Collections.Generic</c>, without allocating a display string.
    /// </summary>
    private static bool IsCollectionsGenericTopLevelType(INamedTypeSymbol type)
        => type.ContainingType is null && IsSystemCollectionsGenericNamespace(type.ContainingNamespace);

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

        if (type is not INamedTypeSymbol { IsGenericType: true, OriginalDefinition: { MetadataName: "KeyValuePair`2" } originalDef } namedType
            || !IsCollectionsGenericTopLevelType(originalDef))
        {
            return false;
        }

        keyType = namedType.TypeArguments[0];
        valueType = namedType.TypeArguments[1];
        return true;
    }

    /// <summary>
    /// Keyed registrations indexed by every type they are assignable to (the implementation type, its interfaces,
    /// and its base classes), built on first use so each key-value dependency does not scan all registrations.
    /// Matches <see cref="IsAssignable"/> with the value type as the target.
    /// </summary>
    private sealed class KeyedServiceIndex(AnalyzerContext analyzerContext)
    {
        private Dictionary<ITypeSymbol, List<KeyedCandidate>>? _byAssignableType;
        private List<KeyedCandidate>? _all;

        private static readonly List<KeyedCandidate> s_noCandidates = [];

        /// <summary>
        /// Gets the keyed registrations assignable to <paramref name="valueType"/>. The returned list must not be modified.
        /// </summary>
        public List<KeyedCandidate> GetCandidates(ITypeSymbol valueType, CancellationToken cancellationToken)
        {
            if (_byAssignableType is null || _all is null)
                Build(cancellationToken);

            // Every type is assignable to object
            if (valueType.SpecialType is SpecialType.System_Object)
                return _all!;

            if (_byAssignableType!.TryGetValue(valueType, out var candidates))
                return candidates;

            // Nullable<T> accepts exactly T, a value type indexed only under itself
            if (valueType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [INamedTypeSymbol underlyingType] }
                && _byAssignableType.TryGetValue(underlyingType, out var underlyingCandidates))
            {
                return underlyingCandidates;
            }

            return s_noCandidates;
        }

        private void Build(CancellationToken cancellationToken)
        {
            // Keyed by runtime type (without tuple element names), like the generator's dependency resolution
            var byAssignableType = new Dictionary<ITypeSymbol, List<KeyedCandidate>>(RuntimeTypeSymbolComparer.Instance);
            var all = new List<KeyedCandidate>();

            foreach (var kvp in analyzerContext.RegisteredServices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Every keyed registration of the type is a candidate, not only the first registration
                var info = kvp.Value;
                foreach (var activation in info.Activations)
                {
                    if (!activation.HasKey)
                        continue;

                    var candidate = new KeyedCandidate(info.Type, activation);
                    all.Add(candidate);
                    Add(info.Type, candidate);

                    foreach (var iface in info.Type.AllInterfaces)
                        Add(iface, candidate);

                    for (var baseType = info.Type.BaseType; baseType is not null && baseType.SpecialType is not SpecialType.System_Object; baseType = baseType.BaseType)
                        Add(baseType, candidate);
                }
            }

            _byAssignableType = byAssignableType;
            _all = all;

            void Add(ITypeSymbol type, KeyedCandidate candidate)
            {
                if (!byAssignableType.TryGetValue(type, out var list))
                {
                    list = [];
                    byAssignableType[type] = list;
                }

                list.Add(candidate);
            }
        }
    }

    /// <summary>
    /// A keyed registration of an implementation type.
    /// </summary>
    private readonly record struct KeyedCandidate(INamedTypeSymbol Type, ServiceActivation Activation)
    {
        public ITypeSymbol? KeyTypeSymbol => Activation.KeyTypeSymbol;
    }
}
