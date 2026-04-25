namespace SourceGen.Ioc.SourceGenerator;

internal static partial class RoslynExtensions
{
    extension(INamedTypeSymbol typeSymbol)
    {
        public IMethodSymbol? PrimaryConstructor
        {
            get
            {
                foreach(var ctor in typeSymbol.Constructors)
                {
                    if(ctor.IsImplicitlyDeclared)
                        continue;

                    var syntaxRef = ctor.DeclaringSyntaxReferences.FirstOrDefault();
                    if(syntaxRef?.GetSyntax() is TypeDeclarationSyntax)
                        return ctor;
                }

                return null;
            }
        }

        public IMethodSymbol? PrimaryOrMostParametersConstructor
        {
            get
            {
                IMethodSymbol? bestCtor = null;
                int maxParameters = -1;
                foreach(var ctor in typeSymbol.Constructors)
                {
                    if(ctor.IsImplicitlyDeclared)
                        continue;

                    var syntaxRef = ctor.DeclaringSyntaxReferences.FirstOrDefault();
                    // Primary constructor
                    if(syntaxRef?.GetSyntax() is TypeDeclarationSyntax)
                        return ctor;

                    if(ctor.IsStatic)
                        continue;

                    if(ctor.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                        continue;

                    // Find constructor with most parameters
                    if(ctor.Parameters.Length > maxParameters)
                    {
                        maxParameters = ctor.Parameters.Length;
                        bestCtor = ctor;
                    }
                }
                return bestCtor;
            }
        }
    }
}