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

        // The key of a keyed invocation is a runtime value, so it closes a keyed open generic registration with its key
        var methodName = memberAccess.Name.Identifier.ValueText;
        var keyKind = methodName is "GetKeyedService" or "GetRequiredKeyedService" or "GetKeyedServices"
            ? DependencyKeyKind.UnknownKeyed
            : DependencyKeyKind.Value;

        return new ServiceProviderInvocation(
            CollectClosedGenericDependencies(typeSymbol, keyKind),
            GetRequestedCollectionElementTypeName(methodName, typeSymbol));
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
    /// Collects the closed generic types a requested type depends on: the type itself and, like constructor parameters
    /// (see <see cref="CollectClosedGenericDependencyFromType"/>), the types its wrappers and collections resolve,
    /// recursively, such as <c>IHandler&lt;Entity&gt;</c> of <c>IEnumerable&lt;KeyValuePair&lt;string, IHandler&lt;Entity&gt;&gt;&gt;</c>.
    /// </summary>
    /// <param name="keyKind">How the requested type is resolved with a key: without one, or by a keyed invocation.</param>
    private static ImmutableEquatableArray<ClosedGenericDependency> CollectClosedGenericDependencies(ITypeSymbol typeSymbol, DependencyKeyKind keyKind)
    {
        // Only closed types are collected, such as IHandler<Entity>[], not IService<> or IHandler<T>
        if(typeSymbol.ContainsGenericParameters)
        {
            return [];
        }

        // Arrays are named after their element type, which is resolved like the element of a collection
        while(typeSymbol is IArrayTypeSymbol arrayTypeSymbol)
        {
            typeSymbol = arrayTypeSymbol.ElementType;
        }

        if(typeSymbol is not INamedTypeSymbol { IsGenericType: true, IsUnboundGenericType: false } namedTypeSymbol)
        {
            return [];
        }

        // Create TypeData with type parameters for closed generic resolution. A requested key value pair (or array of them) is
        // resolved through the key value pairs of every keyed registration, like the element of a collection.
        List<ClosedGenericDependency>? dependencies = null;
        HashSet<(string TypeName, string? Key, DependencyKeyKind KeyKind)>? addedKeys = null;
        CollectClosedGenericDependencyFromType(namedTypeSymbol.CreateBasicTypeData(), key: null, ref dependencies, ref addedKeys, keyKind, isPairOfEveryKey: true);
        return dependencies?.ToImmutableEquatableArray() ?? [];
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
