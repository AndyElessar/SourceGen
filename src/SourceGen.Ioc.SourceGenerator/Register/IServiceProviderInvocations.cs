namespace SourceGen.Ioc.SourceGenerator.Register;

partial class RegisterSourceGenerator
{
    private static bool PredicateInvocations(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "GetService" or "GetRequiredService" or "GetKeyedService" or "GetRequiredKeyedService"
            }
        };

    /// <summary>
    /// Transforms GetService/GetRequiredService/GetKeyedService/GetRequiredKeyedService invocations
    /// to extract closed generic type information for factory registration generation.
    /// </summary>
    /// <remarks>
    /// This method handles the following patterns:
    /// - GetService&lt;T&gt;() / GetRequiredService&lt;T&gt;()
    /// - GetKeyedService&lt;T&gt;(key) / GetRequiredKeyedService&lt;T&gt;(key)
    /// - GetService(typeof(T)) / GetRequiredService(typeof(T))
    /// 
    /// Only closed generic types from open generic registrations are collected.
    /// </remarks>
    private static ClosedGenericDependency? TransformInvocations(GeneratorSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if(context.Node is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        if(invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        var semanticModel = context.SemanticModel;
        ITypeSymbol? typeSymbol = null;

        // Check for generic method invocation: GetService<T>(), GetRequiredService<T>(), etc.
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

        if(typeSymbol is not INamedTypeSymbol namedTypeSymbol)
        {
            return null;
        }

        // Only process closed generic types (has generic arguments but is not open generic)
        // Skip if it's an open generic (e.g., IService<>) or non-generic type
        if(!namedTypeSymbol.IsGenericType || namedTypeSymbol.IsUnboundGenericType)
        {
            return null;
        }

        // Skip if it contains unresolved type parameters (nested open generic)
        if(namedTypeSymbol.ContainsGenericParameters)
        {
            return null;
        }

        // Create TypeData with type parameters for closed generic resolution
        var typeData = namedTypeSymbol.CreateBasicTypeData();

        return new ClosedGenericDependency(
            typeData.Name,
            typeData,
            typeData.NameWithoutGeneric);
    }
}
