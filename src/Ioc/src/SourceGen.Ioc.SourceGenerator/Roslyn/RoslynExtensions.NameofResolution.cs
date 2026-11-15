namespace SourceGen.Ioc;

internal static partial class RoslynExtensions
{
    /// <summary>
    /// Resolves the full access path of a symbol referenced in a nameof() expression.
    /// For example, resolves <c>nameof(Key)</c> to <c>global::Namespace.OuterClass.InnerClass.Key</c>
    /// when Key is a member of InnerClass inside OuterClass.
    /// </summary>
    /// <param name="expression">The expression inside nameof().</param>
    /// <param name="semanticModel">The semantic model to use for symbol resolution.</param>
    /// <returns>The full access path if successfully resolved; otherwise, null.</returns>
    public static string? ResolveNameofExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var symbol = ResolveExpressionSymbol(expression, semanticModel);

        if(symbol is null)
            return null;

        // If the expression already contains a member access (e.g., KeyHolder.Key),
        // we need to resolve it to ensure we get the fully qualified path
        return symbol.FullAccessPath;
    }

    /// <summary>
    /// Finds the first <c>Factory</c>, <c>Instance</c> or C# <c>Key</c> argument of a registration attribute given with
    /// <c>nameof()</c> of a member of an unbound generic type whose type parameters the implementation type does not
    /// declare, such as <c>nameof(Factories&lt;&gt;.Create)</c>: its path would keep the unbound <c>Factories&lt;T&gt;</c>, whose
    /// type arguments cannot be determined. The own unbound type of an open generic implementation type, such as
    /// <c>nameof(Node&lt;&gt;.Default)</c> for <c>Node&lt;T&gt;</c>, is closed with it; a closed implementation type, such as
    /// <c>Node&lt;int&gt;</c>, is never closed, so its own unbound type is not either.
    /// </summary>
    /// <param name="implementation">
    /// The registered implementation type, or <see langword="null"/> for default settings, which apply to other types, so
    /// no unbound generic type is closed.
    /// </param>
    /// <param name="isCsharpKey">Whether the Key is a C# expression (<c>KeyType.Csharp</c>); a value Key is the member's name.</param>
    /// <returns>The argument name, the member and the containing type, or <see langword="null"/> when there is none.</returns>
    public static (string ArgumentName, ExpressionSyntax Argument, ISymbol Member, INamedTypeSymbol ContainingType)? FindNameofOfUnboundGenericType(
        AttributeSyntax attributeSyntax,
        INamedTypeSymbol? implementation,
        bool isCsharpKey,
        SemanticModel semanticModel)
    {
        // Only the type parameters of an open generic implementation are closed per closed registration
        var definition = implementation?.OriginalDefinition;
        var ownDefinition = implementation is not null && definition is not null
            && (implementation.IsUnboundGenericType
                || (definition.TypeParameters.Length > 0 && SymbolEqualityComparer.Default.Equals(implementation, definition)))
            ? definition
            : null;

        foreach(var argument in attributeSyntax.ArgumentList?.Arguments ?? default)
        {
            var name = argument.NameEquals?.Name.Identifier.ValueText;
            if(name is not ("Factory" or "Instance" or "Key") || (name is "Key" && !isCsharpKey))
                continue;

            // Only an unbound generic type, such as Factories<>, names type parameters that are not in scope, so the
            // argument is bound only then
            if(argument.Expression is not InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: [var nameofArgument] }
                || !nameofArgument.Expression.DescendantNodes().OfType<OmittedTypeArgumentSyntax>().Any()
                || ResolveExpressionSymbol(nameofArgument.Expression, semanticModel) is not { } member)
            {
                continue;
            }

            for(var containingType = member.ContainingType; containingType is not null; containingType = containingType.ContainingType)
            {
                if(UsesOtherTypeParameters(containingType, ownDefinition))
                    return (name, argument.Expression, member, containingType);
            }
        }

        return null;

        // A member looked up on an unbound generic type has error type arguments, which are displayed as the type parameters
        // of its definition, so only the implementation type's own definition is closed with its type arguments
        static bool UsesOtherTypeParameters(ITypeSymbol type, INamedTypeSymbol? implementation) => type switch
        {
            INamedTypeSymbol namedType when namedType.IsUnboundGenericType || namedType.TypeArguments.Any(static typeArgument => typeArgument.TypeKind is TypeKind.Error)
                => implementation is null || !SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, implementation),
            ITypeParameterSymbol typeParameter => implementation is null || !SymbolEqualityComparer.Default.Equals(typeParameter.DeclaringType?.OriginalDefinition, implementation),
            INamedTypeSymbol namedType => namedType.TypeArguments.Any(typeArgument => UsesOtherTypeParameters(typeArgument, implementation)),
            IArrayTypeSymbol arrayType => UsesOtherTypeParameters(arrayType.ElementType, implementation),
            _ => false,
        };
    }

    /// <summary>
    /// Resolves the method symbol from a nameof() or string expression in an attribute.
    /// </summary>
    /// <param name="expression">The expression inside nameof() or a string literal.</param>
    /// <param name="semanticModel">The semantic model to use for symbol resolution.</param>
    /// <returns>The method symbol if found; otherwise, null.</returns>
    public static IMethodSymbol? ResolveMethodSymbol(ExpressionSyntax expression, SemanticModel semanticModel)
        => ResolveExpressionSymbol(expression, semanticModel) as IMethodSymbol;

    /// <summary>
    /// Resolves the symbol an expression refers to, or its first candidate.
    /// </summary>
    /// <remarks>
    /// Speculative binding binds only the expression at its own position, so names are looked up in the same scope.
    /// <see cref="ModelExtensions.GetSymbolInfo(SemanticModel, SyntaxNode, CancellationToken)"/> binds the whole
    /// enclosing attribute instead, and transforms receive a new semantic model on every run, so that work repeats
    /// on every edit. It remains the fallback when speculative binding finds nothing.
    /// </remarks>
    private static ISymbol? ResolveExpressionSymbol(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var model = semanticModel.ForTreeOf(expression);
        if(expression is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax name } memberAccess
            && ResolveSingleTypeMember(model, memberAccess.Expression, name.Identifier.ValueText) is { } member)
            return member;

        var speculative = model.GetSpeculativeSymbolInfo(expression.SpanStart, expression, SpeculativeBindingOption.BindAsExpression);
        if((speculative.Symbol ?? speculative.CandidateSymbols.FirstOrDefault()) is { } speculativeSymbol)
            return speculativeSymbol;

        var symbolInfo = model.GetSymbolInfo(expression);
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
    }

    /// <summary>
    /// Resolves <c>Type.Member</c> by binding only the qualifier as a type and looking the member up by name.
    /// </summary>
    /// <remarks>
    /// Binding the whole expression of a method group, such as <c>nameof(Factory.Create)</c>, runs overload resolution
    /// that fails on every edit and costs about three times as much. Returns null, so the caller binds the expression,
    /// unless the qualifier is a type and its hierarchy has exactly one member with the name; overloads and hidden members
    /// are left to the compiler's lookup.
    /// </remarks>
    private static ISymbol? ResolveSingleTypeMember(SemanticModel model, ExpressionSyntax qualifier, string memberName)
    {
        if(model.GetSpeculativeSymbolInfo(qualifier.SpanStart, qualifier, SpeculativeBindingOption.BindAsTypeOrNamespace).Symbol
            is not INamedTypeSymbol type)
            return null;

        ISymbol? found = null;
        for(ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach(var member in current.GetMembers(memberName))
            {
                if(found is not null)
                    return null;
                found = member;
            }
        }

        return found;
    }

    extension(SemanticModel semanticModel)
    {
        /// <summary>
        /// Returns a semantic model that can bind <paramref name="node"/>.
        /// </summary>
        /// <remarks>
        /// A transform's semantic model belongs to the file of its target node, but symbols reached through it, such as
        /// members of a partial type declared in another file, carry attribute syntax from other trees. Binding such a
        /// node with the transform's model throws <see cref="ArgumentException"/>.
        /// </remarks>
        public SemanticModel ForTreeOf(SyntaxNode node) => node.SyntaxTree == semanticModel.SyntaxTree
            ? semanticModel
            : semanticModel.Compilation.GetSemanticModel(node.SyntaxTree);
    }
}
