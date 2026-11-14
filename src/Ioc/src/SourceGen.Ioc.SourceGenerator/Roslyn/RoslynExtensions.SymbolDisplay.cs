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
        /// Builds the fully qualified access path for a symbol, including namespace and containing types. <br/>
        /// For example, for a field <c>Key</c> inside <c>NestClassImpl</c> inside <c>TestNestClass</c> in namespace <c>MyApp.Services</c>,
        /// returns <c>global::MyApp.Services.TestNestClass.NestClassImpl.Key</c>.
        /// The type arguments of the containing types are kept, such as <c>global::MyApp.Holder&lt;int&gt;.Default</c>
        /// or <c>global::MyApp.Node&lt;T&gt;.Default</c>.
        /// </summary>
        /// <returns>The fully qualified access path for the symbol.</returns>
        public string FullAccessPath
        {
            get
            {
                if(symbol.ContainingType is { } containingType)
                {
                    return $"{containingType.FullyQualifiedName}.{symbol.Name}";
                }

                var containingNamespace = symbol.ContainingNamespace;
                return containingNamespace is not null && !containingNamespace.IsGlobalNamespace
                    ? $"{containingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{symbol.Name}"
                    : $"global::{symbol.Name}";
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