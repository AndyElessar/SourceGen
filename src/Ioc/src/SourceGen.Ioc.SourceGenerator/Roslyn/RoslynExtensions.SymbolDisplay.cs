namespace SourceGen.Ioc.SourceGenerator;

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
        /// </summary>
        /// <returns>The fully qualified access path for the symbol.</returns>
        public string FullAccessPath
        {
            get
            {
                // Build path from the symbol up to its containing types (collect in reverse order, then reverse)
                List<string> pathParts = [symbol.Name];

                var containingType = symbol.ContainingType;
                while(containingType is not null)
                {
                    pathParts.Add(containingType.Name);
                    containingType = containingType.ContainingType;
                }

                // Reverse to get correct order (outermost type first)
                pathParts.Reverse();

                // Add namespace prefix with global::
                var containingNamespace = symbol.ContainingType?.ContainingNamespace ?? symbol.ContainingNamespace;
                if(containingNamespace is not null && !containingNamespace.IsGlobalNamespace)
                {
                    var namespacePath = containingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return $"{namespacePath}.{string.Join(".", pathParts)}";
                }

                // For global namespace, just prepend global::
                return $"global::{string.Join(".", pathParts)}";
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