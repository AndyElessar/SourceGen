namespace SourceGen.Ioc;

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

            var dependency = CreateClosedGenericDependency(typeSymbol);
            if(dependency.HasValue)
            {
                yield return dependency.Value;
            }
        }
    }

    /// <summary>
    /// Transforms generic DiscoverAttribute (DiscoverAttribute&lt;T&gt;) to extract closed generic type information.
    /// The type is specified via type parameter instead of constructor argument.
    /// </summary>
    private static IEnumerable<ClosedGenericDependency> TransformDiscoverGeneric(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach(var attribute in context.Attributes)
        {
            var attrClass = attribute.AttributeClass;
            if(attrClass?.IsGenericType != true || attrClass.TypeArguments.Length == 0)
            {
                continue;
            }

            if(attrClass.TypeArguments[0] is not INamedTypeSymbol typeSymbol)
            {
                continue;
            }

            var dependency = CreateClosedGenericDependency(typeSymbol);
            if(dependency.HasValue)
            {
                yield return dependency.Value;
            }
        }
    }

    /// <summary>
    /// Creates a ClosedGenericDependency from a type symbol if it's a valid closed generic type.
    /// </summary>
    private static ClosedGenericDependency? CreateClosedGenericDependency(INamedTypeSymbol typeSymbol)
    {
        // Only process closed generic types (has generic arguments but is not open generic)
        // Skip if it's an open generic (e.g., IService<>) or non-generic type
        if(!typeSymbol.IsGenericType || typeSymbol.IsUnboundGenericType)
        {
            return null;
        }

        // Skip if it contains unresolved type parameters (nested open generic)
        if(typeSymbol.ContainsGenericParameters)
        {
            return null;
        }

        // Create TypeData with type parameters for closed generic resolution
        var typeData = typeSymbol.CreateBasicTypeData();

        // Return the type as a dependency
        return new ClosedGenericDependency(
            typeData.Name,
            typeData,
            typeData.NameWithoutGeneric);
    }
}
