namespace SourceGen.Ioc;

internal static partial class TransformExtensions
{
    extension(INamedTypeSymbol typeSymbol)
    {
        /// <summary>
        /// Gets whether generated code compiled into <paramref name="assembly"/> can construct the type: a struct, or a
        /// non-abstract, non-static class with an instance constructor (including the implicit default constructor) that is
        /// accessible from <paramref name="assembly"/> (see <see cref="IsAccessibleConstructor"/>).
        /// </summary>
        public bool IsConstructibleIn(IAssemblySymbol assembly) => typeSymbol.TypeKind switch
        {
            TypeKind.Struct => true,
            TypeKind.Class => !typeSymbol.IsAbstract && !typeSymbol.IsStatic
                && typeSymbol.OriginalDefinition.InstanceConstructors.Any(ctor => IsAccessibleConstructor(ctor, assembly)),
            TypeKind.Error => true,
            _ => false,
        };

        /// <summary>
        /// Gets the constructor generated code calls, selected by priority among the public and internal constructors:
        /// [Inject] marked, primary, most parameters.
        /// </summary>
        /// <param name="assembly">
        /// The assembly the generated code is compiled into; only constructors accessible from it are selected (see
        /// <see cref="IsAccessibleConstructor"/>). Without it (type data that does not need to be constructed, such as a
        /// decorator matched to its service types), public and internal constructors are selected.
        /// </param>
        public IMethodSymbol? GetSelectedConstructor(IAssemblySymbol? assembly)
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

                if(assembly is null
                    ? ctor.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)
                    : !IsAccessibleConstructor(ctor, assembly))
                {
                    continue;
                }

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

        /// <summary>
        /// Extracts constructor parameters from a type and indicates whether the constructor was selected by [Inject] attribute.
        /// </summary>
        /// <param name="semanticModel">Optional semantic model for resolving nameof() expressions in service keys.</param>
        /// <param name="assembly">The assembly the generated code is compiled into; see <see cref="GetSelectedConstructor"/>.</param>
        /// <returns>The constructor parameters, whether the constructor has [Inject] attribute, and the selected constructor.</returns>
        public (ImmutableEquatableArray<ParameterData> Parameters, bool HasInjectConstructor, IMethodSymbol? Constructor) ExtractConstructorParametersWithInfo(
            SemanticModel? semanticModel = null,
            IAssemblySymbol? assembly = null)
        {
            // Get the original definition for open generic types to access constructors
            var typeToInspect = typeSymbol.IsGenericType && typeSymbol.IsDefinition
                ? typeSymbol
                : typeSymbol.OriginalDefinition ?? typeSymbol;

            // Get the constructor: [Inject] marked > primary constructor > most parameters
            var constructor = typeToInspect.GetSelectedConstructor(assembly);
            if(constructor is null)
            {
                return ([], false, null);
            }

            // Check if the selected constructor has [IocInject] or [Inject] attribute
            bool hasInjectConstructor = constructor.GetAttributes()
                .Any(static attr => attr.AttributeClass?.IsInject == true);

            using var parameters = new ValueListBuilder<ParameterData>(constructor.Parameters.Length);
            foreach(var param in constructor.Parameters)
            {
                var paramType = param.Type;

                // Dependencies resolve through their own registrations; only generic/collection metadata is needed here.
                // Wrappers (Lazy, Func, collections, Dictionary, ...) expose their element through wrapper metadata,
                // so their interface hierarchy (e.g. ~10 interfaces for Dictionary<,>) is only built for other generic types.
                var paramTypeData = paramType.GetTypeData();
                if(paramType is INamedTypeSymbol { IsGenericType: true } namedParamType && paramTypeData is not WrapperTypeData)
                {
                    paramTypeData = paramTypeData with
                    {
                        AllInterfaces = namedParamType.GetAllInterfaces(),
                        AllBaseClasses = namedParamType.GetAllBaseClasses(),
                    };
                }

                // Check if parameter type is nullable (e.g., IDependency?)
                var isNullable = param.NullableAnnotation == NullableAnnotation.Annotated;

                // Check if parameter has an explicit default value (for skipping unresolvable parameters)
                var hasDefaultValue = param.HasExplicitDefaultValue;

                // Check for [FromKeyedServices], [Inject], or [ServiceKey] attribute
                // SemanticModel is used to resolve nameof() expressions for top-level parameters
                var (serviceKey, hasInjectAttribute, hasServiceKeyAttribute, hasFromKeyedServicesAttribute) = param.GetServiceKeyAndAttributeInfo(semanticModel);

                // Get the C# code representation of the default value
                var defaultValue = hasDefaultValue ? ToDefaultValueCodeString(param.ExplicitDefaultValue, param.Type) : null;

                parameters.Add(new ParameterData(
                    param.EscapedName,
                    paramTypeData,
                    IsNullable: isNullable,
                    HasDefaultValue: hasDefaultValue,
                    DefaultValue: defaultValue,
                    ServiceKey: serviceKey,
                    HasInjectAttribute: hasInjectAttribute,
                    HasServiceKeyAttribute: hasServiceKeyAttribute,
                    HasFromKeyedServicesAttribute: hasFromKeyedServicesAttribute));
            }

            return (parameters.ToImmutableEquatableArray(), hasInjectConstructor, constructor);
        }
    }

    /// <summary>
    /// Gets whether generated code compiled into <paramref name="assembly"/> can call the constructor: it is public, or
    /// internal (or protected internal) and declared in <paramref name="assembly"/> or in an assembly that gives it access
    /// to its internals (<c>InternalsVisibleTo</c>).
    /// </summary>
    private static bool IsAccessibleConstructor(IMethodSymbol ctor, IAssemblySymbol assembly) => ctor.DeclaredAccessibility switch
    {
        Accessibility.Public => true,
        Accessibility.Internal or Accessibility.ProtectedOrInternal =>
            SymbolEqualityComparer.Default.Equals(ctor.ContainingAssembly, assembly) || ctor.ContainingAssembly.GivesAccessTo(assembly),
        _ => false,
    };
}
