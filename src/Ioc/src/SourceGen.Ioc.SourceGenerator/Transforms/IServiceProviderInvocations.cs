namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static bool PredicateInvocations(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "GetService" or "GetRequiredService" or "GetKeyedService" or "GetRequiredKeyedService" or "GetServices" or "GetKeyedServices"
            }
        };

    /// <summary>
    /// Transforms GetService/GetRequiredService/GetKeyedService/GetRequiredKeyedService/GetServices invocations
    /// into the dependency information that both closed-generic resolution and collection resolver generation use.
    /// The requested type is resolved once and shared by both.
    /// </summary>
    /// <remarks>
    /// This method handles the following patterns: <br/>
    /// - GetService&lt;T&gt;() / GetRequiredService&lt;T&gt;() <br/>
    /// - GetKeyedService&lt;T&gt;(key) / GetRequiredKeyedService&lt;T&gt;(key) <br/>
    /// - GetServices&lt;T&gt;() / GetKeyedServices&lt;T&gt;(key) <br/>
    /// - GetService(typeof(T)) / GetRequiredService(typeof(T)) <br/>
    /// - GetKeyedService(typeof(T), key) / GetRequiredKeyedService(typeof(T), key) <br/>
    /// - GetServices(typeof(T)) / GetKeyedServices(typeof(T), key) <br/>
    /// </remarks>
    private static ServiceProviderInvocation TransformInvocation(GeneratorSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if(context.Node is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess } invocation)
        {
            return ServiceProviderInvocation.Empty;
        }

        var semanticModel = context.SemanticModel;
        ITypeSymbol? typeSymbol = null;

        // Check for generic method invocation: GetService<T>(), GetRequiredService<T>(), GetServices<T>(), etc.
        if(memberAccess.Name is GenericNameSyntax genericName && genericName.TypeArgumentList.Arguments.Count == 1)
        {
            typeSymbol = ResolveTypeSyntax(genericName.TypeArgumentList.Arguments[0], semanticModel, ct);
        }
        // Check for typeof() argument: GetService(typeof(T)), GetRequiredService(typeof(T)), etc.
        else if(invocation.ArgumentList.Arguments.Count > 0)
        {
            var firstArg = invocation.ArgumentList.Arguments[0].Expression;
            if(firstArg is TypeOfExpressionSyntax typeOfExpr)
            {
                typeSymbol = ResolveTypeSyntax(typeOfExpr.Type, semanticModel, ct);
            }
        }

        if(typeSymbol is null)
        {
            return ServiceProviderInvocation.Empty;
        }

        return new ServiceProviderInvocation(
            CollectClosedGenericDependencies(typeSymbol).ToImmutableEquatableArray(),
            GetRequestedCollectionElementTypeName(memberAccess.Name.Identifier.ValueText, typeSymbol));
    }

    /// <summary>
    /// Gets the element type requested as a non-keyed collection: <c>GetServices&lt;T&gt;()</c> / <c>GetServices(typeof(T))</c>,
    /// or <c>GetService</c> / <c>GetRequiredService</c> of <c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c>,
    /// <c>ICollection&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, <c>IList&lt;T&gt;</c> or <c>T[]</c>.
    /// Keyed collections are resolved through GetKeyedServices, not the generated collection resolvers, so they are skipped.
    /// </summary>
    private static string? GetRequestedCollectionElementTypeName(string methodName, ITypeSymbol typeSymbol)
    {
        if(typeSymbol.ContainsGenericParameters)
            return null;

        if(methodName == "GetServices")
            return typeSymbol.FullyQualifiedName;

        if(methodName is not ("GetService" or "GetRequiredService"))
            return null;

        return typeSymbol.TryGetWrapperInfo(out var wrapperInfo) && wrapperInfo.Kind.IsCollectionWrapperKind() && wrapperInfo.ElementType is { } elementType
            ? elementType.FullyQualifiedName
            : null;
    }

    /// <summary>
    /// Collects the closed generic types a requested type depends on.
    /// Only closed generic types are collected; for wrapper and collection types the inner/element type is also extracted.
    /// </summary>
    private static IEnumerable<ClosedGenericDependency> CollectClosedGenericDependencies(ITypeSymbol typeSymbol)
    {
        // Handle array types (e.g., GetService<IHandler<T>[]>())
        if(typeSymbol is IArrayTypeSymbol arrayTypeSymbol)
        {
            var elementTypeSymbol = arrayTypeSymbol.ElementType;
            if(elementTypeSymbol is INamedTypeSymbol namedElementType
                && namedElementType.IsGenericType
                && !namedElementType.IsUnboundGenericType
                && !namedElementType.ContainsGenericParameters)
            {
                var elementTypeData = namedElementType.CreateBasicTypeData();
                if(elementTypeData is not GenericTypeData genericElementTypeData)
                {
                    yield break;
                }

                yield return new ClosedGenericDependency(
                    elementTypeData.Name,
                    elementTypeData,
                    genericElementTypeData.NameWithoutGeneric);
            }
            yield break;
        }

        if(typeSymbol is not INamedTypeSymbol namedTypeSymbol)
        {
            yield break;
        }

        // Only process closed generic types (has generic arguments but is not open generic)
        // Skip if it's an open generic (e.g., IService<>) or non-generic type
        if(!namedTypeSymbol.IsGenericType || namedTypeSymbol.IsUnboundGenericType)
        {
            yield break;
        }

        // Skip if it contains unresolved type parameters (nested open generic)
        if(namedTypeSymbol.ContainsGenericParameters)
        {
            yield break;
        }

        // Create TypeData with type parameters for closed generic resolution
        var typeData = namedTypeSymbol.CreateBasicTypeData();

        // Extract inner types from wrapper types for closed generic dependency discovery
        var wrapperInnerType = typeData switch
        {
            LazyTypeData l => l.InstanceType,
            FuncTypeData f => f.ReturnType,
            DictionaryTypeData d => d.ValueType,
            KeyValuePairTypeData k => k.ValueType,
            _ => (TypeData?)null
        };
        if(wrapperInnerType is GenericTypeData { GenericArity: > 0, IsOpenGeneric: false, IsNestedOpenGeneric: false } genericInnerType)
        {
            yield return new ClosedGenericDependency(
                wrapperInnerType.Name,
                wrapperInnerType,
                genericInnerType.NameWithoutGeneric);
        }

        // Check if this is a collection type — extract element type for closed generic dependency
        var collectionElementType = typeData switch
        {
            CollectionWrapperTypeData c => c.ElementType,
            _ => null
        };
        if(collectionElementType is GenericTypeData { GenericArity: > 0, IsOpenGeneric: false, IsNestedOpenGeneric: false } genericElementType)
        {
            // Yield the element type as a dependency (e.g., IHandler<T> from IEnumerable<IHandler<T>>)
            yield return new ClosedGenericDependency(
                collectionElementType.Name,
                collectionElementType,
                genericElementType.NameWithoutGeneric);
        }

        // Yield the original type as a dependency
        if(typeData is not GenericTypeData genericTypeData)
        {
            yield break;
        }

        yield return new ClosedGenericDependency(
            typeData.Name,
            typeData,
            genericTypeData.NameWithoutGeneric);
    }

    /// <summary>
    /// Resolves the type a type syntax refers to.
    /// </summary>
    /// <remarks>
    /// Speculative binding binds only the type at its own position, so names are looked up in the same scope.
    /// <see cref="ModelExtensions.GetTypeInfo(SemanticModel, SyntaxNode, CancellationToken)"/> binds the whole
    /// enclosing statement instead, and this transform reruns with a new semantic model for every invocation on every
    /// edit. It remains the fallback when speculative binding finds nothing.
    /// </remarks>
    private static ITypeSymbol? ResolveTypeSyntax(TypeSyntax type, SemanticModel semanticModel, CancellationToken ct)
    {
        var speculative = semanticModel.GetSpeculativeTypeInfo(type.SpanStart, type, SpeculativeBindingOption.BindAsTypeOrNamespace).Type;
        if(speculative is not null and not IErrorTypeSymbol)
            return speculative;

        return semanticModel.GetTypeInfo(type, ct).Type;
    }
}
