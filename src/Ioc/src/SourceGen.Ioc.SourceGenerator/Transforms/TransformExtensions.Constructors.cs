namespace SourceGen.Ioc.SourceGenerator.Models;

internal static partial class TransformExtensions
{
    extension(INamedTypeSymbol typeSymbol)
    {
        public IMethodSymbol? SpecifiedOrPrimaryOrMostParametersConstructor
        {
            get
            {
                IMethodSymbol? injectCtor = null;
                IMethodSymbol? primaryCtor = null;
                IMethodSymbol? bestCtor = null;
                int maxParameters = -1;
                foreach(var ctor in typeSymbol.Constructors)
                {
                    if(ctor.IsImplicitlyDeclared)
                        continue;

                    if(ctor.IsStatic)
                        continue;

                    if(ctor.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                        continue;

                    // IocInjectAttribute/InjectAttribute specified constructor - highest priority
                    if(ctor.GetAttributes().Any(attr => attr.AttributeClass?.IsInject == true))
                    {
                        injectCtor = ctor;
                        continue;
                    }

                    var syntaxRef = ctor.DeclaringSyntaxReferences.FirstOrDefault();
                    // Primary constructor - second priority
                    if(syntaxRef?.GetSyntax() is TypeDeclarationSyntax)
                    {
                        primaryCtor = ctor;
                        continue;
                    }

                    // Find constructor with most parameters - lowest priority
                    if(ctor.Parameters.Length > maxParameters)
                    {
                        maxParameters = ctor.Parameters.Length;
                        bestCtor = ctor;
                    }
                }

                // Return by priority: [Inject] > primary > most parameters
                return injectCtor ?? primaryCtor ?? bestCtor;
            }
        }

        /// <summary>
        /// Extracts constructor parameters from a type and indicates whether the constructor was selected by [Inject] attribute.
        /// </summary>
        /// <param name="visited">Set of visited types to prevent infinite recursion.</param>
        /// <param name="semanticModel">Optional semantic model for resolving nameof() expressions in service keys. Only used for top-level extraction, not passed to recursive calls.</param>
        /// <returns>A tuple containing the constructor parameters and whether the constructor has [Inject] attribute.</returns>
        public (ImmutableEquatableArray<ParameterData> Parameters, bool HasInjectConstructor) ExtractConstructorParametersWithInfo(
            HashSet<INamedTypeSymbol>? visited = null,
            SemanticModel? semanticModel = null)
        {
            // Check if we've already visited this type to prevent infinite recursion
            if(visited is not null && !visited.Add(typeSymbol))
            {
                return ([], false);
            }

            // Get the original definition for open generic types to access constructors
            var typeToInspect = typeSymbol.IsGenericType && typeSymbol.IsDefinition
                ? typeSymbol
                : typeSymbol.OriginalDefinition ?? typeSymbol;

            // Get the constructor: [Inject] marked > primary constructor > most parameters
            var constructor = typeToInspect.SpecifiedOrPrimaryOrMostParametersConstructor;
            if(constructor is null)
            {
                return ([], false);
            }

            // Check if the selected constructor has [IocInject] or [Inject] attribute
            bool hasInjectConstructor = constructor.GetAttributes()
                .Any(static attr => attr.AttributeClass?.IsInject == true);

            visited ??= new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            List<ParameterData> parameters = [];
            foreach(var param in constructor.Parameters)
            {
                var paramType = param.Type;

                // Get TypeData using the unified method with recursive constructor extraction
                // Also extract hierarchy (interfaces) for generic types to enable IEnumerable<T> detection
                var paramTypeData = paramType is INamedTypeSymbol namedParamType
                    ? namedParamType.GetTypeData(extractConstructorParams: true, extractHierarchy: namedParamType.IsGenericType, visited: visited)
                    : paramType.GetTypeData();

                // Check if parameter type is nullable (e.g., IDependency?)
                var isNullable = param.NullableAnnotation == NullableAnnotation.Annotated;

                // Check if parameter has an explicit default value (for skipping unresolvable parameters)
                var hasDefaultValue = param.HasExplicitDefaultValue;

                // Check for [FromKeyedServices], [Inject], or [ServiceKey] attribute
                // SemanticModel is used to resolve nameof() expressions for top-level parameters
                var (serviceKey, hasInjectAttribute, hasServiceKeyAttribute, hasFromKeyedServicesAttribute) = param.GetServiceKeyAndAttributeInfo(semanticModel);

                // Get the C# code representation of the default value
                var defaultValue = hasDefaultValue ? ToDefaultValueCodeString(param.ExplicitDefaultValue) : null;

                parameters.Add(new ParameterData(
                    param.Name,
                    paramTypeData,
                    IsNullable: isNullable,
                    HasDefaultValue: hasDefaultValue,
                    DefaultValue: defaultValue,
                    ServiceKey: serviceKey,
                    HasInjectAttribute: hasInjectAttribute,
                    HasServiceKeyAttribute: hasServiceKeyAttribute,
                    HasFromKeyedServicesAttribute: hasFromKeyedServicesAttribute));
            }

            return (parameters.ToImmutableEquatableArray(), hasInjectConstructor);
        }
    }
}