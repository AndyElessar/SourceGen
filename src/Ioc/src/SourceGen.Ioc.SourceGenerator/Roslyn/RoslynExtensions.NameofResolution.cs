namespace SourceGen.Ioc.SourceGenerator;

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
        var symbolInfo = semanticModel.GetSymbolInfo(expression);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

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
    {
        var symbolInfo = semanticModel.GetSymbolInfo(expression);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

        return symbol as IMethodSymbol;
    }
}