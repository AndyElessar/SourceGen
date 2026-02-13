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
    /// to extract closed generic type information for factory registration generation.
    /// </summary>
    /// <remarks>
    /// This method handles the following patterns: <br/>
    /// - GetService&lt;T&gt;() / GetRequiredService&lt;T&gt;() <br/>
    /// - GetKeyedService&lt;T&gt;(key) / GetRequiredKeyedService&lt;T&gt;(key) <br/>
    /// - GetServices&lt;T&gt;() / GetKeyedServices&lt;T&gt;(key) <br/>
    /// - GetService(typeof(T)) / GetRequiredService(typeof(T)) <br/>
    /// - GetKeyedService(typeof(T), key) / GetRequiredKeyedService(typeof(T), key) <br/>
    /// - GetServices(typeof(T)) / GetKeyedServices(typeof(T), key) <br/>
    /// Only closed generic types from open generic registrations are collected. <br/>
    /// For collection types (IEnumerable&lt;T&gt;, IList&lt;T&gt;, etc.), the element type T is also extracted. <br/>
    /// </remarks>
    private static IEnumerable<ClosedGenericDependency> TransformInvocations(GeneratorSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if(context.Node is not InvocationExpressionSyntax invocation)
        {
            yield break;
        }

        if(invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            yield break;
        }

        var semanticModel = context.SemanticModel;
        ITypeSymbol? typeSymbol = null;

        // Check for generic method invocation: GetService<T>(), GetRequiredService<T>(), GetServices<T>(), etc.
        if(memberAccess.Name is GenericNameSyntax genericName && genericName.TypeArgumentList.Arguments.Count == 1)
        {
            var typeArgSyntax = genericName.TypeArgumentList.Arguments[0];
            var typeInfo = semanticModel.GetTypeInfo(typeArgSyntax, ct);
            typeSymbol = typeInfo.Type;
        }
        // Check for typeof() argument: GetService(typeof(T)), GetRequiredService(typeof(T)), etc.
        else if(invocation.ArgumentList.Arguments.Count > 0)
        {
            var firstArg = invocation.ArgumentList.Arguments[0].Expression;
            if(firstArg is TypeOfExpressionSyntax typeOfExpr)
            {
                var typeInfo = semanticModel.GetTypeInfo(typeOfExpr.Type, ct);
                typeSymbol = typeInfo.Type;
            }
        }

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

        // Check if this is a collection type — extract element type for closed generic dependency
        if(typeData is CollectionTypeData collectionType
            && collectionType.ElementType is GenericTypeData { GenericArity: > 0, IsOpenGeneric: false, IsNestedOpenGeneric: false } genericElementType)
        {
            // Yield the element type as a dependency (e.g., IHandler<T> from IEnumerable<IHandler<T>>)
            yield return new ClosedGenericDependency(
                collectionType.ElementType.Name,
                collectionType.ElementType,
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
}
