using System.Globalization;

namespace SourceGen.Ioc.SourceGenerator;

/// <summary>
/// Extension methods for Roslyn symbol manipulation.
/// </summary>
internal static class RoslynExtensions
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

        public bool ContainsGenericParameters
        {
            get
            {
                if(typeSymbol.TypeKind is TypeKind.TypeParameter or TypeKind.Error)
                {
                    return true;
                }

                if(typeSymbol is INamedTypeSymbol namedTypeSymbol)
                {
                    if(namedTypeSymbol.IsUnboundGenericType)
                    {
                        return true;
                    }

                    for(; namedTypeSymbol != null; namedTypeSymbol = namedTypeSymbol.ContainingType)
                    {
                        if(namedTypeSymbol.TypeArguments.Any(arg => arg.ContainsGenericParameters))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        public INamedTypeSymbol? GetCompatibleGenericBaseType([NotNullWhen(true)] INamedTypeSymbol? genericType)
        {
            if(genericType is null)
            {
                return null;
            }

            Debug.Assert(genericType.IsGenericTypeDefinition);

            if(genericType.TypeKind is TypeKind.Interface)
            {
                foreach(INamedTypeSymbol interfaceType in typeSymbol.AllInterfaces)
                {
                    if(IsMatchingGenericType(interfaceType, genericType))
                    {
                        return interfaceType;
                    }
                }
            }

            for(INamedTypeSymbol? current = typeSymbol as INamedTypeSymbol; current != null; current = current.BaseType)
            {
                if(IsMatchingGenericType(current, genericType))
                {
                    return current;
                }
            }

            return null;

            static bool IsMatchingGenericType(INamedTypeSymbol candidate, INamedTypeSymbol baseType)
            {
                return candidate.IsGenericType && SymbolEqualityComparer.Default.Equals(candidate.ConstructedFrom, baseType);
            }
        }

        public TypeData GetTypeData(
            bool extractConstructorParams = false,
            bool extractHierarchy = false,
            HashSet<INamedTypeSymbol>? visited = null)
        {
            if(typeSymbol is INamedTypeSymbol namedTypeSymbol)
                return namedTypeSymbol.GetTypeData(extractConstructorParams, extractHierarchy, visited);

            // Handle array types specially - extract element type information
            if(typeSymbol is IArrayTypeSymbol arrayTypeSymbol)
                return arrayTypeSymbol.GetTypeData(extractConstructorParams, extractHierarchy, visited);

            var name = typeSymbol.FullyQualifiedName;
            return new TypeData(
                name,
                GetNameWithoutGeneric(name),
                typeSymbol.ContainsGenericParameters,
                0,
                false);
        }
    }

    extension(INamedTypeSymbol typeSymbol)
    {
        public bool IsGenericTypeDefinition => typeSymbol is { IsGenericType: true, IsDefinition: true };

        /// <summary>
        /// Gets the type parameters source for this type symbol.
        /// For unbound generic types, returns parameters from the original definition.
        /// </summary>
        public ImmutableArray<ITypeParameterSymbol> TypeParametersSource =>
            typeSymbol.IsUnboundGenericType
                ? typeSymbol.OriginalDefinition?.TypeParameters ?? typeSymbol.TypeParameters
                : typeSymbol.TypeParameters;

        /// <summary>
        /// Gets the type data for this type symbol.
        /// </summary>
        /// <param name="extractConstructorParams">Whether to extract constructor parameters recursively.</param>
        /// <param name="extractHierarchy">Whether to extract all interfaces and base classes.</param>
        public TypeData GetTypeData(
            bool extractConstructorParams = false,
            bool extractHierarchy = false,
            HashSet<INamedTypeSymbol>? visited = null)
        {
            visited = extractConstructorParams
                ? (visited ?? new(SymbolEqualityComparer.Default))
                : null;

            // Build type name - for unbound generics, use actual type parameter names
            var typeName = typeSymbol.BuildTypeName();

            // Extract type parameters with full constraints
            ImmutableEquatableArray<TypeParameter>? typeParameters = null;
            if(typeSymbol.IsGenericType && typeSymbol.TypeArguments.Length > 0)
            {
                typeParameters = typeSymbol.ExtractTypeParameters(extractConstraints: true, depth: 0);
            }

            ImmutableEquatableArray<ParameterData>? constructorParams = null;
            bool hasInjectConstructor = false;
            if(extractConstructorParams && visited is not null)
            {
                (constructorParams, hasInjectConstructor) = typeSymbol.ExtractConstructorParametersWithInfo(visited);
            }

            // Extract hierarchy (interfaces and base classes) if requested
            ImmutableEquatableArray<TypeData>? allInterfaces = null;
            ImmutableEquatableArray<TypeData>? allBaseClasses = null;
            if(extractHierarchy)
            {
                allInterfaces = typeSymbol.GetAllInterfaces();
                allBaseClasses = typeSymbol.GetAllBaseClasses();
            }

            // Check if this is a non-IEnumerable collection type that requires factory method for DI
            var nameWithoutGeneric = GetNameWithoutGeneric(typeName);
            var isNonEnumerableCollection = IsNonEnumerableCollectionType(nameWithoutGeneric);

            return new TypeData(
                typeName,
                nameWithoutGeneric,
                typeSymbol.ContainsGenericParameters,
                typeSymbol.Arity,
                typeSymbol.IsNestedOpenGeneric,
                IsTypeParameter: false, // Named types are not type parameters
                isNonEnumerableCollection,
                typeParameters,
                constructorParams,
                hasInjectConstructor,
                allInterfaces,
                allBaseClasses);
        }

        /// <summary>
        /// Builds the fully qualified type name for this type symbol.
        /// For unbound generic types, uses actual type parameter names instead of empty placeholders.
        /// </summary>
        public string BuildTypeName()
        {
            // For unbound generic types (e.g., typeof(Handler<,>)), we need to get the 
            // type parameter names from TypeParameters, not from FullyQualifiedName
            // FullyQualifiedName returns "global::Ns.Handler<,>" but we need "global::Ns.Handler<TRequest, TResponse>"
            if(typeSymbol.IsUnboundGenericType && typeSymbol.TypeParametersSource.Length > 0)
            {
                var nameWithoutGeneric = GetNameWithoutGeneric(typeSymbol.FullyQualifiedName);
                var typeParamNames = typeSymbol.TypeParametersSource.Select(tp => tp.Name);
                return $"{nameWithoutGeneric}<{string.Join(", ", typeParamNames)}>";
            }

            return typeSymbol.FullyQualifiedName;
        }

        /// <summary>
        /// Core implementation for extracting type parameters.
        /// </summary>
        /// <param name="extractConstraints">Whether to extract constraint types for each type parameter.</param>
        /// <param name="depth">Current recursion depth to prevent infinite recursion.</param>
        /// <returns>An immutable array of type parameters with their resolved types.</returns>
        public ImmutableEquatableArray<TypeParameter> ExtractTypeParameters(bool extractConstraints, int depth)
        {
            const int MaxDepth = 10; // Prevent infinite recursion for pathological cases

            var typeParams = typeSymbol.TypeParametersSource;
            if(typeParams.Length == 0 || depth >= MaxDepth)
            {
                return [];
            }

            var typeArgs = typeSymbol.TypeArguments;
            List<TypeParameter> parameters = new(typeParams.Length);

            for(int i = 0; i < typeParams.Length; i++)
            {
                var typeParam = typeParams[i];
                var typeArg = i < typeArgs.Length ? typeArgs[i] : null;

                // Create TypeData for the type argument
                var (typeData, allInterfaces) = typeParam.CreateTypeDataForTypeArg(typeArg, depth);

                // Add interfaces if extracted
                if(allInterfaces is { Length: > 0 })
                {
                    typeData = typeData with { AllInterfaces = allInterfaces };
                }

                // Extract constraints only when requested (to avoid recursion in basic scenarios)
                ImmutableEquatableArray<TypeData>? constraintTypes = null;
                if(extractConstraints)
                {
                    constraintTypes = typeParam.ConstraintTypes
                        .Select(ct => ct is INamedTypeSymbol namedCt
                            ? namedCt.CreateBasicTypeData(depth + 1)
                            : new TypeData(ct.FullyQualifiedName, GetNameWithoutGeneric(ct.FullyQualifiedName), ct.ContainsGenericParameters, 0, false))
                        .ToImmutableEquatableArray();
                }

                parameters.Add(new TypeParameter(
                    typeParam.Name,
                    typeData,
                    constraintTypes,
                    typeParam.HasValueTypeConstraint,
                    typeParam.HasReferenceTypeConstraint,
                    typeParam.HasUnmanagedTypeConstraint,
                    typeParam.HasNotNullConstraint,
                    typeParam.HasConstructorConstraint));
            }

            return parameters.ToImmutableEquatableArray();
        }

        /// <summary>
        /// Gets all interfaces implemented by a type.
        /// Creates basic TypeData without recursive type parameter extraction to avoid circular dependencies.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetAllInterfaces() =>
            typeSymbol.AllInterfaces.Select(CreateBasicTypeData).ToImmutableEquatableArray();

        /// <summary>
        /// Gets all base classes of a type, excluding System.Object.
        /// Creates basic TypeData without recursive type parameter extraction to avoid circular dependencies.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetAllBaseClasses()
        {
            List<TypeData> result = [];
            var baseType = typeSymbol.BaseType;
            while(baseType != null && baseType.SpecialType != SpecialType.System_Object)
            {
                result.Add(baseType.CreateBasicTypeData());
                baseType = baseType.BaseType;
            }
            return result.ToImmutableEquatableArray();
        }

        /// <summary>
        /// Creates a basic TypeData with type parameters extracted recursively.
        /// Does not extract constraint types to avoid circular dependencies.
        /// </summary>
        public TypeData CreateBasicTypeData(int depth = 0)
        {
            var typeName = typeSymbol.FullyQualifiedName;
            var nameWithoutGeneric = GetNameWithoutGeneric(typeName);

            // Extract type parameters without constraints (to avoid recursion)
            ImmutableEquatableArray<TypeParameter>? typeParameters = null;
            if(typeSymbol.IsGenericType && typeSymbol.TypeArguments.Length > 0)
            {
                typeParameters = typeSymbol.ExtractTypeParameters(extractConstraints: false, depth);
            }

            // Check if this is a non-IEnumerable collection type
            var isNonEnumerableCollection = IsNonEnumerableCollectionType(nameWithoutGeneric);

            return new TypeData(
                typeName,
                nameWithoutGeneric,
                typeSymbol.ContainsGenericParameters,
                typeSymbol.Arity,
                typeSymbol.IsNestedOpenGeneric,
                IsTypeParameter: false, // Named types are not type parameters
                isNonEnumerableCollection,
                typeParameters);
        }

        /// <summary>
        /// Determines whether the type is a nested open generic type.
        /// A nested open generic is a generic type where any type argument itself contains generic parameters.
        /// For example: IGeneric&lt;IGeneric2&lt;T&gt;&gt; is a nested open generic.
        /// But IGeneric&lt;T&gt; or IGeneric&lt;int&gt; are not.
        /// </summary>
        public bool IsNestedOpenGeneric
        {
            get
            {
                if(!typeSymbol.IsGenericType)
                {
                    return false;
                }

                // For unbound generic types (e.g., IRepository<>), TypeArguments contains error types
                // which should not be considered as nested open generics
                if(typeSymbol.IsUnboundGenericType)
                {
                    return false;
                }

                // Check if any type argument contains generic parameters
                foreach(var typeArg in typeSymbol.TypeArguments)
                {
                    // If the type argument is not a simple type parameter (T, T1, etc.)
                    // but contains generic parameters, it's a nested open generic
                    if(typeArg.TypeKind != TypeKind.TypeParameter && typeArg.ContainsGenericParameters)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

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

                    // InjectAttribute specified constructor - highest priority
                    if(ctor.GetAttributes().Any(attr => attr.AttributeClass?.Name == "InjectAttribute"))
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
        /// <returns>A tuple containing the constructor parameters and whether the constructor has [Inject] attribute.</returns>
        public (ImmutableEquatableArray<ParameterData> Parameters, bool HasInjectConstructor) ExtractConstructorParametersWithInfo(
            HashSet<INamedTypeSymbol>? visited = null)
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

            // Check if the selected constructor has [Inject] attribute
            bool hasInjectConstructor = constructor.GetAttributes()
                .Any(static attr => attr.AttributeClass?.Name == "InjectAttribute");

            visited ??= new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            List<ParameterData> parameters = [];
            foreach(var param in constructor.Parameters)
            {
                var paramType = param.Type;

                // Get TypeData using the unified method with recursive constructor extraction
                var paramTypeData = paramType is INamedTypeSymbol namedParamType
                    ? namedParamType.GetTypeData(extractConstructorParams: true, visited: visited)
                    : paramType.GetTypeData();

                // Check if parameter is optional (has default value or is nullable)
                var isOptional = param.HasExplicitDefaultValue || param.NullableAnnotation == NullableAnnotation.Annotated;

                // Check for [FromKeyedServices], [Inject], or [ServiceKey] attribute
                var (serviceKey, hasInjectAttribute, hasServiceKeyAttribute) = param.GetServiceKeyAndAttributeInfo();

                parameters.Add(new ParameterData(param.Name, paramTypeData, IsOptional: isOptional,
                    ServiceKey: serviceKey, HasInjectAttribute: hasInjectAttribute, HasServiceKeyAttribute: hasServiceKeyAttribute));
            }

            return (parameters.ToImmutableEquatableArray(), hasInjectConstructor);
        }
    }

    extension(IArrayTypeSymbol arrayTypeSymbol)
    {
        public TypeData GetTypeData(
            bool extractConstructorParams = false,
            bool extractHierarchy = false,
            HashSet<INamedTypeSymbol>? visited = null)
        {
            var elementType = arrayTypeSymbol.ElementType;
            var typeName = arrayTypeSymbol.FullyQualifiedName;

            // For arrays, create TypeData with element type as a pseudo-TypeParameter
            // This allows TryGetArrayElementType to extract the element type
            ImmutableEquatableArray<TypeParameter> typeParameters;
            if(elementType is INamedTypeSymbol namedElementType)
            {
                var elementTypeData = namedElementType.GetTypeData(extractConstructorParams, extractHierarchy, visited);
                typeParameters = [new TypeParameter("T", elementTypeData)];
            }
            else
            {
                var elementTypeName = elementType.FullyQualifiedName;
                var elementTypeData = new TypeData(
                    elementTypeName,
                    GetNameWithoutGeneric(elementTypeName),
                    elementType.ContainsGenericParameters,
                    0,
                    IsNestedOpenGeneric: false,
                    IsTypeParameter: elementType.TypeKind == TypeKind.TypeParameter);
                typeParameters = [new TypeParameter("T", elementTypeData)];
            }

            return new TypeData(
                typeName,
                typeName, // For arrays, use full name as NameWithoutGeneric
                elementType.ContainsGenericParameters,
                GenericArity: 1, // Arrays have one "type parameter" (the element type)
                IsNestedOpenGeneric: false,
                IsTypeParameter: false,
                IsNonEnumerableCollection: true, // Arrays require factory method for DI
                typeParameters);
        }
    }

    extension(IParameterSymbol param)
    {
        /// <summary>
        /// Gets the service key, injection attribute info, and [ServiceKey] attribute from a parameter.
        /// [FromKeyedServices] takes precedence over [Inject] for service key resolution.
        /// HasInjectAttribute is only true for [Inject] attribute (not [FromKeyedServices], which MS.DI handles automatically).
        /// HasServiceKeyAttribute indicates the parameter is marked with [ServiceKey] from Microsoft.Extensions.DependencyInjection.
        /// </summary>
        /// <returns>A tuple containing the service key (if any), whether the parameter has [Inject] attribute, and whether it has [ServiceKey] attribute.</returns>
        public (string? ServiceKey, bool HasInjectAttribute, bool HasServiceKeyAttribute) GetServiceKeyAndAttributeInfo()
        {
            string? serviceKey = null;
            bool hasInjectAttribute = false;
            bool hasServiceKeyAttribute = false;

            foreach(var attribute in param.GetAttributes())
            {
                var attrClass = attribute.AttributeClass;
                if(attrClass is null)
                    continue;

                // Check for Microsoft.Extensions.DependencyInjection.ServiceKeyAttribute
                if(attrClass.Name == "ServiceKeyAttribute"
                    && attrClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
                {
                    hasServiceKeyAttribute = true;
                    continue;
                }

                // Check for Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute (higher priority for key)
                // Note: [FromKeyedServices] is handled by MS.DI automatically, so we don't set hasInjectAttribute
                if(attrClass.Name == "FromKeyedServicesAttribute"
                    && attrClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
                {
                    // The key is the first constructor argument
                    if(attribute.ConstructorArguments.Length > 0)
                    {
                        var keyArg = attribute.ConstructorArguments[0];
                        if(!keyArg.IsNull && keyArg.Value is not null)
                        {
                            serviceKey = keyArg.GetPrimitiveConstantString();
                        }
                    }
                    // [FromKeyedServices] found, but continue to check for [Inject] as well
                    continue;
                }

                // Check for InjectAttribute (by name only, to support third-party attributes)
                if(attrClass.Name == "InjectAttribute")
                {
                    hasInjectAttribute = true;
                    // Only use [Inject] key if no [FromKeyedServices] key was found
                    if(serviceKey is null)
                    {
                        var (key, _) = attribute.GetKey();
                        serviceKey = key;
                    }
                }
            }
            return (serviceKey, hasInjectAttribute, hasServiceKeyAttribute);
        }
    }

    extension(ITypeParameterSymbol typeParam)
    {
        /// <summary>
        /// Creates TypeData for a type argument, with optional interface extraction.
        /// </summary>
        public (TypeData TypeData, ImmutableEquatableArray<TypeData>? AllInterfaces) CreateTypeDataForTypeArg(
            ITypeSymbol? typeArg,
            int depth)
        {
            if(typeArg is INamedTypeSymbol namedArg && typeArg.TypeKind != TypeKind.TypeParameter)
            {
                // For concrete types, recursively extract type parameters and interfaces
                var typeData = namedArg.CreateBasicTypeData(depth + 1);
                var allInterfaces = namedArg.AllInterfaces.Length > 0
                    ? namedArg.AllInterfaces.Select(CreateInterfaceTypeData).ToImmutableEquatableArray()
                    : null;
                return (typeData, allInterfaces);
            }

            if(typeArg is not null)
            {
                var argName = typeArg.FullyQualifiedName;
                var isTypeParam = typeArg.TypeKind == TypeKind.TypeParameter;
                var typeData = new TypeData(
                    argName,
                    GetNameWithoutGeneric(argName),
                    typeArg.ContainsGenericParameters,
                    0,
                    IsNestedOpenGeneric: false,
                    IsTypeParameter: isTypeParam);
                return (typeData, null);
            }

            // No type argument available, this is a type parameter placeholder
            return (new TypeData(typeParam.Name, typeParam.Name, true, 0, IsNestedOpenGeneric: false, IsTypeParameter: true), null);

            // Creates a simple TypeData for an interface type.
            static TypeData CreateInterfaceTypeData(INamedTypeSymbol iface) =>
                new(
                    iface.FullyQualifiedName,
                    GetNameWithoutGeneric(iface.FullyQualifiedName),
                    iface.ContainsGenericParameters,
                    iface.Arity,
                    false);
        }
    }

    extension(AttributeData attributeData)
    {
        /// <summary>
        /// Gets a named argument value from an attribute data.
        /// </summary>
        public T? GetNamedArgument<T>(string name, T? defaultValue = default)
        {
            foreach(var namedArg in attributeData.NamedArguments)
            {
                if(namedArg.Key == name)
                {
                    if(namedArg.Value.IsNull)
                    {
                        return defaultValue;
                    }

                    return (T?)namedArg.Value.Value;
                }
            }

            return defaultValue;
        }

        /// <summary>
        /// Tries to get a named argument value from an attribute data.<br/>
        /// If the argument is not found, returns HasArg = false.
        /// </summary>
        public (bool HasArg, T? Value) TryGetNamedArgument<T>(string name, T? defaultValue = default)
        {
            foreach(var namedArg in attributeData.NamedArguments)
            {
                if(namedArg.Key == name)
                {
                    if(namedArg.Value.IsNull)
                    {
                        return (true, defaultValue);
                    }

                    return (true, (T?)namedArg.Value.Value);
                }
            }

            return (false, defaultValue);
        }

        /// <summary>
        /// Checks if a named argument was explicitly set in an attribute.
        /// </summary>
        public bool HasNamedArgument(string name)
        {
            foreach(var namedArg in attributeData.NamedArguments)
            {
                if(namedArg.Key == name)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets an array of type symbols from a named argument.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetTypeArrayArgument(string name, bool extractConstructorParams = false)
        {
            foreach(var namedArg in attributeData.NamedArguments)
            {
                if(namedArg.Key.Equals(name, StringComparison.Ordinal) && !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    List<TypeData> result = [];
                    foreach(var value in namedArg.Value.Values)
                    {
                        if(value.Value is ITypeSymbol typeSymbol)
                        {
                            result.Add(typeSymbol.GetTypeData(extractConstructorParams));
                        }
                    }
                    return result.ToImmutableEquatableArray();
                }
            }

            return [];
        }

        /// <summary>
        /// Gets an array of type symbols from an attribute constructor argument of type <c>params Type[]</c>.
        /// This is the constructor-argument counterpart to <see cref="GetTypeArrayArgument"/>, and is used when
        /// service types are supplied positionally to the attribute constructor instead of via a named <c>Type[]</c> argument.
        /// </summary>
        /// <remarks>
        /// This method scans the attribute's constructor arguments for an array (or <c>params</c>) argument that contains
        /// type values (for example, <c>params Type[] serviceTypes</c>) and converts those <see cref="ITypeSymbol"/> instances
        /// to <see cref="TypeData"/>. It skips non-type arguments, such as <c>ServiceLifetime</c> enum values.
        /// </remarks>
        public ImmutableEquatableArray<TypeData> GetTypeArrayFromConstructorArgument(bool extractConstructorParams = false)
        {
            foreach(var ctorArg in attributeData.ConstructorArguments)
            {
                // Look for an array argument containing type values
                if(ctorArg.Kind == TypedConstantKind.Array && !ctorArg.IsNull)
                {
                    List<TypeData> result = [];
                    foreach(var value in ctorArg.Values)
                    {
                        if(value.Value is ITypeSymbol typeSymbol)
                        {
                            result.Add(typeSymbol.GetTypeData(extractConstructorParams));
                        }
                    }

                    // Only return if we found type values
                    if(result.Count > 0)
                        return result.ToImmutableEquatableArray();
                }
            }

            return [];
        }

        /// <summary>
        /// Tries to get the original syntax for a named argument, especially for <see langword="nameof"/> expressions.
        /// When a <see cref="SemanticModel"/> is provided, resolves the full access path of the referenced symbol.
        /// </summary>
        /// <param name="argumentName">The name of the argument to find.</param>
        /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
        /// <returns>The resolved symbol path if it's a <see langword="nameof"/> expression; otherwise, null.</returns>
        public string? TryGetNameof(string argumentName, SemanticModel? semanticModel = null)
        {
            var syntaxReference = attributeData.ApplicationSyntaxReference;
            if(syntaxReference is null)
                return null;

            var syntax = syntaxReference.GetSyntax();
            if(syntax is not AttributeSyntax attributeSyntax)
                return null;

            var argumentList = attributeSyntax.ArgumentList;
            if(argumentList is null)
                return null;

            foreach(var argument in argumentList.Arguments)
            {
                // Check if this is a named argument with the correct name
                if(argument.NameEquals?.Name.Identifier.Text == argumentName)
                {
                    // Check if the expression is a nameof() invocation
                    if(argument.Expression is InvocationExpressionSyntax invocation &&
                       invocation.Expression is IdentifierNameSyntax identifierName &&
                       identifierName.Identifier.Text == "nameof")
                    {
                        // Extract the argument inside nameof() and return just that expression
                        if(invocation.ArgumentList.Arguments.Count == 1)
                        {
                            var nameofArgument = invocation.ArgumentList.Arguments[0].Expression;

                            // If semantic model is provided, try to resolve the full access path
                            if(semanticModel is not null)
                            {
                                var resolvedPath = ResolveNameofExpression(nameofArgument, semanticModel);
                                if(resolvedPath is not null)
                                    return resolvedPath;
                            }

                            return nameofArgument.ToFullString().Trim();
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Tries to extract the <see langword="nameof"/> expression from a constructor argument of an attribute.
        /// </summary>
        /// <param name="argumentIndex">The index of the constructor argument to check.</param>
        /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
        /// <returns>The resolved symbol path if it's a <see langword="nameof"/> expression; otherwise, null.</returns>
        public string? TryGetNameofFromConstructorArg(int argumentIndex, SemanticModel? semanticModel = null)
        {
            var syntaxReference = attributeData.ApplicationSyntaxReference;
            if(syntaxReference is null)
                return null;

            var syntax = syntaxReference.GetSyntax();
            if(syntax is not AttributeSyntax attributeSyntax)
                return null;

            var argumentList = attributeSyntax.ArgumentList;
            if(argumentList is null || argumentList.Arguments.Count <= argumentIndex)
                return null;

            var argument = argumentList.Arguments[argumentIndex];

            // Skip named arguments (they don't count as constructor arguments)
            if(argument.NameEquals is not null)
                return null;

            // Check if the expression is a nameof() invocation
            if(argument.Expression is InvocationExpressionSyntax invocation &&
               invocation.Expression is IdentifierNameSyntax identifierName &&
               identifierName.Identifier.Text == "nameof")
            {
                // Extract the argument inside nameof() and return just that expression
                if(invocation.ArgumentList.Arguments.Count == 1)
                {
                    var nameofArgument = invocation.ArgumentList.Arguments[0].Expression;

                    // If semantic model is provided, try to resolve the full access path
                    if(semanticModel is not null)
                    {
                        var resolvedPath = ResolveNameofExpression(nameofArgument, semanticModel);
                        if(resolvedPath is not null)
                            return resolvedPath;
                    }

                    return nameofArgument.ToFullString().Trim();
                }
            }

            return null;
        }
    }

    extension(TypedConstant constant)
    {
        public string GetPrimitiveConstantString() => FormatPrimitiveConstant(constant.Type, constant.Value);
    }

    private static string GetNameWithoutGeneric(string typeName)
    {
        int angleIndex = typeName.IndexOf('<');
        return angleIndex > 0 ? typeName[..angleIndex] : typeName;
    }

    public static string FormatPrimitiveConstant(ITypeSymbol? type, object? value)
    {
        if(type?.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T)
        {
            var elementType = ((INamedTypeSymbol)type).TypeArguments[0];
            return value is null ? "null" : FormatPrimitiveConstant(elementType, value);
        }

        if(type?.TypeKind is TypeKind.Enum)
        {
            return FormatEnumLiteral((INamedTypeSymbol)type, value!);
        }

        return value switch
        {
            null => type?.IsNullable is null or true ? "null!" : "default",
            false => "false",
            true => "true",

            string s => SymbolDisplay.FormatLiteral(s, quote: true),
            char c => SymbolDisplay.FormatLiteral(c, quote: true),

            double.NaN => "double.NaN",
            double.NegativeInfinity => "double.NegativeInfinity",
            double.PositiveInfinity => "double.PositiveInfinity",
            double d => $"{d.ToString("G17", CultureInfo.InvariantCulture)}d",

            float.NaN => "float.NaN",
            float.NegativeInfinity => "float.NegativeInfinity",
            float.PositiveInfinity => "float.PositiveInfinity",
            float f => $"{f.ToString("G9", CultureInfo.InvariantCulture)}f",

            decimal d => $"{d.ToString(CultureInfo.InvariantCulture)}m",

            // Must be one of the other numeric types or an enum
            object num => Convert.ToString(num, CultureInfo.InvariantCulture),
        };

        static string FormatEnumLiteral(INamedTypeSymbol enumType, object value)
        {
            Debug.Assert(enumType.TypeKind is TypeKind.Enum);

            foreach(ISymbol member in enumType.GetMembers())
            {
                if(member is IFieldSymbol { IsConst: true, ConstantValue: { } constantValue } field)
                {
                    if(Equals(constantValue, value))
                    {
                        return FormatEnumField(field);
                    }
                }
            }

            bool isFlagsEnum = enumType.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == "FlagsAttribute" &&
                attr.AttributeClass.ContainingNamespace.ToDisplayString() == "System");

            if(isFlagsEnum)
            {
                // Convert the value to ulong for bitwise operations
                ulong numericValue = ConvertToUInt64(value);
                var fields = enumType.GetMembers().OfType<IFieldSymbol>()
                    .Select((f, i) => (Index: i, Symbol: f, NumericValue: ConvertToUInt64(f.ConstantValue!)))
                    .ToArray();

                // Check for any zero numeric values.
                if(numericValue == 0)
                {
                    foreach(var field in fields)
                    {
                        if(field.NumericValue == 0)
                        {
                            return FormatEnumField(field.Symbol);
                        }
                    }
                }
                else
                {
                    List<int>? matches = null;
                    foreach(var field in fields.OrderByDescending(f => f.NumericValue))
                    {
                        // Greedy match of flag values from highest to lowest numeric value.
                        if(field.NumericValue != 0 && (numericValue & field.NumericValue) == field.NumericValue)
                        {
                            (matches ??= []).Add(field.Index);
                            numericValue &= ~field.NumericValue;
                            if(numericValue == 0)
                            {
                                break; // All bits accounted for
                            }
                        }
                    }

                    if(numericValue == 0)
                    {
                        matches!.Sort(); // Format components using the original declaration order.
                        return string.Join(" | ", matches.Select(i => FormatEnumField(fields[i].Symbol)));
                    }
                }
            }

            // Value does not correspond to any combination of defined constants, just cast the numeric value.
            return $"({enumType.FullyQualifiedName})({Convert.ToString(value, CultureInfo.InvariantCulture)!})";

            static string FormatEnumField(IFieldSymbol field)
            {
                return $"{field.ContainingType.FullyQualifiedName}.{field.Name}";
            }

            static ulong ConvertToUInt64(object value)
            {
                return value switch
                {
                    byte b => b,
                    sbyte sb => (ulong)sb,
                    short s => (ulong)s,
                    ushort us => us,
                    char c => c,
                    int i => (ulong)i,
                    uint ui => ui,
                    long l => (ulong)l,
                    ulong ul => ul,
                    _ => 0
                };
            }
        }
    }

    public static string GetSafeNamespace(string name) => string.IsNullOrWhiteSpace(name) ? "Generated" : name;

    public static string GetSafeMethodName(string name)
    {
        if(string.IsNullOrWhiteSpace(name))
            return "Generated";

        StringBuilder builder = new(name.Length + 1);
        for(int i = 0; i < name.Length; i++)
        {
            char ch = name[i];
            if(i == 0 && char.IsDigit(ch))
            {
                builder.Append('_');
            }

            if(char.IsLetterOrDigit(ch) || ch == '_')
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append('_');
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Collection type names (without generic part) that are compatible with IEnumerable&lt;T&gt;.
    /// These types are resolved by DI as collections of the element type.
    /// </summary>
    private static readonly HashSet<string> s_enumerableCompatibleTypes = new(StringComparer.Ordinal)
    {
        "global::System.Collections.Generic.IEnumerable",
        "global::System.Collections.Generic.ICollection",
        "global::System.Collections.Generic.IList",
        "global::System.Collections.Generic.IReadOnlyCollection",
        "global::System.Collections.Generic.IReadOnlyList",
        "System.Collections.Generic.IEnumerable",
        "System.Collections.Generic.ICollection",
        "System.Collections.Generic.IList",
        "System.Collections.Generic.IReadOnlyCollection",
        "System.Collections.Generic.IReadOnlyList",
        "IEnumerable",
        "ICollection",
        "IList",
        "IReadOnlyCollection",
        "IReadOnlyList"
    };

    /// <summary>
    /// Collection type names (without generic part) that require factory method for DI injection.
    /// MS.DI only supports automatic injection for IEnumerable&lt;T&gt;, not these types.
    /// </summary>
    private static readonly HashSet<string> s_nonEnumerableCollectionTypes = new(StringComparer.Ordinal)
    {
        "global::System.Collections.Generic.ICollection",
        "global::System.Collections.Generic.IList",
        "global::System.Collections.Generic.IReadOnlyCollection",
        "global::System.Collections.Generic.IReadOnlyList",
        "global::System.Collections.Generic.List",
        "System.Collections.Generic.ICollection",
        "System.Collections.Generic.IList",
        "System.Collections.Generic.IReadOnlyCollection",
        "System.Collections.Generic.IReadOnlyList",
        "System.Collections.Generic.List",
        "ICollection",
        "IList",
        "IReadOnlyCollection",
        "IReadOnlyList",
        "List"
    };

    /// <summary>
    /// Checks if the given type name (without generic part) is compatible with IEnumerable&lt;T&gt;.
    /// </summary>
    public static bool IsEnumerableCompatibleType(string nameWithoutGeneric) =>
        s_enumerableCompatibleTypes.Contains(nameWithoutGeneric);

    /// <summary>
    /// Checks if the given type name (without generic part) is a non-IEnumerable collection type.
    /// These types require factory method for DI injection.
    /// </summary>
    public static bool IsNonEnumerableCollectionType(string nameWithoutGeneric) =>
        s_nonEnumerableCollectionTypes.Contains(nameWithoutGeneric);

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

    extension<T>(IEnumerable<T> source)
    {
        public IEnumerable<(int Index, T Item)> Index()
        {
            int index = 0;
            foreach(var item in source)
            {
                yield return (index, item);
                checked { index++; }
            }
        }
    }

    extension<T>(IReadOnlyList<T> source)
    {
        public IEnumerable<(int Index, T Item)> Index()
        {
            for(int i = 0; i < source.Count; i++)
            {
                yield return (i, source[i]);
            }
        }
    }

    #region Type Parameter Substitution

    /// <summary>
    /// Substitutes multiple type parameters in a type name with actual type arguments.
    /// Uses Span-based processing to minimize string allocations.
    /// </summary>
    /// <param name="typeName">The type name containing type parameters to substitute.</param>
    /// <param name="typeArgMap">A map of type parameter names to their actual type arguments.</param>
    /// <returns>The type name with all type parameters substituted.</returns>
    public static string SubstituteTypeArguments(string typeName, TypeArgMap typeArgMap)
    {
        if(typeArgMap.IsEmpty)
        {
            return typeName;
        }

        // Fast path: check if any substitution is needed
        var typeNameSpan = typeName.AsSpan();
        bool needsSubstitution = false;
        foreach(var (key, _) in typeArgMap)
        {
            if(ContainsTypeParameter(typeNameSpan, key.AsSpan()))
            {
                needsSubstitution = true;
                break;
            }
        }

        if(!needsSubstitution)
        {
            return typeName;
        }

        return SubstituteTypeArgumentsCore(typeNameSpan, typeArgMap.AsSpan());
    }

    /// <summary>
    /// Replaces a single type parameter with an actual type argument in a type name.
    /// </summary>
    /// <param name="typeName">The type name containing the type parameter.</param>
    /// <param name="typeParam">The type parameter name to replace (e.g., "T").</param>
    /// <param name="actualArg">The actual type argument to substitute (e.g., "string").</param>
    /// <returns>The type name with the type parameter replaced.</returns>
    public static string ReplaceTypeParameter(string typeName, string typeParam, string actualArg)
    {
        var typeNameSpan = typeName.AsSpan();

        // Fast path: check if substitution is needed
        if(!ContainsTypeParameter(typeNameSpan, typeParam.AsSpan()))
        {
            return typeName;
        }

        // Delegate to core implementation with single-element span
        Span<TypeArgEntry> singleEntry = [new(typeParam, actualArg)];
        return SubstituteTypeArgumentsCore(typeNameSpan, singleEntry);
    }

    /// <summary>
    /// Core implementation for type parameter substitution.
    /// Performs all substitutions in a single pass using StringBuilder.
    /// </summary>
    /// <param name="typeNameSpan">The type name span to process.</param>
    /// <param name="sortedEntries">Entries sorted by key length descending for correct matching priority.</param>
    /// <returns>The type name with all type parameters substituted.</returns>
    private static string SubstituteTypeArgumentsCore(
        ReadOnlySpan<char> typeNameSpan,
        ReadOnlySpan<TypeArgEntry> sortedEntries)
    {
        var result = new StringBuilder(typeNameSpan.Length + 32);
        int i = 0;

        while(i < typeNameSpan.Length)
        {
            // Check if current position is a valid identifier start
            bool isValidStart = i == 0 || !IsIdentifierChar(typeNameSpan[i - 1]);

            if(isValidStart && TryMatchTypeParameter(typeNameSpan, i, sortedEntries, out var match, out int matchLength))
            {
                result.Append(match);
                i += matchLength;
            }
            else
            {
                result.Append(typeNameSpan[i]);
                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Tries to match a type parameter at the given position.
    /// </summary>
    /// <returns>True if a match was found, with the replacement value and match length.</returns>
    private static bool TryMatchTypeParameter(
        ReadOnlySpan<char> typeNameSpan,
        int position,
        ReadOnlySpan<TypeArgEntry> sortedEntries,
        [NotNullWhen(true)] out string? replacement,
        out int matchLength)
    {
        foreach(var (key, value) in sortedEntries)
        {
            var typeParamSpan = key.AsSpan();
            int paramLength = typeParamSpan.Length;

            if(position + paramLength <= typeNameSpan.Length &&
               typeNameSpan.Slice(position, paramLength).SequenceEqual(typeParamSpan))
            {
                // Check if it's a whole word (ends at identifier boundary)
                bool isEnd = position + paramLength == typeNameSpan.Length
                                || !IsIdentifierChar(typeNameSpan[position + paramLength]);

                if(isEnd)
                {
                    replacement = value;
                    matchLength = paramLength;
                    return true;
                }
            }
        }

        replacement = null;
        matchLength = 0;
        return false;
    }

    /// <summary>
    /// Checks if the type name contains the type parameter as a whole word.
    /// </summary>
    private static bool ContainsTypeParameter(ReadOnlySpan<char> typeName, ReadOnlySpan<char> typeParam)
    {
        int index = 0;
        while(index <= typeName.Length - typeParam.Length)
        {
            int pos = typeName[index..].IndexOf(typeParam, StringComparison.Ordinal);
            if(pos < 0)
            {
                return false;
            }

            int absolutePos = index + pos;
            bool isStart = absolutePos == 0
                            || !IsIdentifierChar(typeName[absolutePos - 1]);
            bool isEnd = absolutePos + typeParam.Length == typeName.Length
                            || !IsIdentifierChar(typeName[absolutePos + typeParam.Length]);

            if(isStart && isEnd)
            {
                return true;
            }

            index = absolutePos + 1;
        }
        return false;
    }

    /// <summary>
    /// Checks if a character can be part of an identifier.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    #endregion
}
