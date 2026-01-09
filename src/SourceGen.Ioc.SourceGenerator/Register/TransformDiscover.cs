namespace SourceGen.Ioc.SourceGenerator.Register;

partial class RegisterSourceGenerator
{
    /// <summary>
    /// Transforms DiscoverAttribute to extract closed generic type information for factory registration generation.
    /// </summary>
    /// <remarks>
    /// This handles the pattern:
    /// <code>
    /// [Discover(typeof(IRequestHandler&lt;TestRequest&lt;string&gt;, List&lt;string&gt;&gt;))]
    /// public void DoAction()
    /// {
    ///     var response = Mediator.Send(new TestRequest&lt;string&gt;());
    /// }
    /// </code>
    /// The DiscoverAttribute allows users to explicitly declare closed generic types that should be resolved
    /// from open generic registrations, even when they are not directly referenced in constructor parameters
    /// or GetService calls.
    /// </remarks>
    private static IEnumerable<ClosedGenericDependency> TransformDiscover(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach(var attribute in context.Attributes)
        {
            // Get the ClosedGenericType from constructor argument
            if(attribute.ConstructorArguments.Length == 0)
            {
                continue;
            }

            var typeArg = attribute.ConstructorArguments[0];
            if(typeArg.Value is not INamedTypeSymbol typeSymbol)
            {
                continue;
            }

            // Only process closed generic types (has generic arguments but is not open generic)
            // Skip if it's an open generic (e.g., IService<>) or non-generic type
            if(!typeSymbol.IsGenericType || typeSymbol.IsUnboundGenericType)
            {
                continue;
            }

            // Skip if it contains unresolved type parameters (nested open generic)
            if(typeSymbol.ContainsGenericParameters)
            {
                continue;
            }

            // Create TypeData with type parameters for closed generic resolution
            var typeData = typeSymbol.CreateBasicTypeData();

            // Yield the type as a dependency
            yield return new ClosedGenericDependency(
                typeData.Name,
                typeData,
                typeData.NameWithoutGeneric);
        }
    }
}
