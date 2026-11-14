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
