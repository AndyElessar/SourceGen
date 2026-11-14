namespace SourceGen.Ioc;

/// <summary>
/// Extension methods for Roslyn symbol manipulation.
/// </summary>
internal static partial class RoslynExtensions
{
    /// <param name="symbol">The symbol</param>
    extension(ISymbol symbol)
    {
        /// <summary>
        /// Gets the symbol's name as an identifier generated code can write, with <c>@</c> before a keyword, such as
        /// <c>@event</c> for a property declared as <c>@event</c>. Contextual keywords, such as <c>value</c>, are kept.
        /// </summary>
        public string EscapedName => SyntaxFacts.GetKeywordKind(symbol.Name) is SyntaxKind.None ? symbol.Name : $"@{symbol.Name}";

        /// <summary>
        /// Builds the fully qualified access path for a symbol, including namespace and containing types. <br/>
        /// For example, for a field <c>Key</c> inside <c>NestClassImpl</c> inside <c>TestNestClass</c> in namespace <c>MyApp.Services</c>,
        /// returns <c>global::MyApp.Services.TestNestClass.NestClassImpl.Key</c>.
        /// The type arguments of the containing types are kept, such as <c>global::MyApp.Holder&lt;int&gt;.Default</c>
        /// or <c>global::MyApp.Node&lt;T&gt;.Default</c>, and a name that is a keyword is escaped, such as <c>@default</c>.
        /// </summary>
        /// <returns>The fully qualified access path for the symbol.</returns>
        public string FullAccessPath
        {
            get
            {
                var name = symbol.EscapedName;
                if(symbol.ContainingType is { } containingType)
                {
                    return $"{containingType.FullyQualifiedName}.{name}";
                }

                var containingNamespace = symbol.ContainingNamespace;
                return containingNamespace is not null && !containingNamespace.IsGlobalNamespace
                    ? $"{containingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{name}"
                    : $"global::{name}";
            }
        }
    }

    extension(ITypeSymbol typeSymbol)
    {
        /// <summary>
        /// Gets the fully qualified name of a type symbol.
        /// </summary>
        public string FullyQualifiedName => typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        public bool IsNullable => !typeSymbol.IsValueType || typeSymbol.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T;
    }
}