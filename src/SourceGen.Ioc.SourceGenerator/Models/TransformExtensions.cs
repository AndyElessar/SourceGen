namespace SourceGen.Ioc.SourceGenerator.Models;

internal static class TransformExtensions
{
    private static string GetNameWithoutGeneric(string typeName)
    {
        int angleIndex = typeName.IndexOf('<');
        return angleIndex > 0 ? typeName[..angleIndex] : typeName;
    }

    extension(ITypeSymbol typeSymbol)
    {
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
                IsNestedOpenGeneric: false,
                IsTypeParameter: false,
                CollectionKind: CollectionKind.None,
                IsBuiltInTypeOrBuiltInCollection: typeSymbol.IsBuiltInTypeOrBuiltInCollection);
        }
    }

    extension(INamedTypeSymbol typeSymbol)
    {
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

            // Check if this is a collection type for DI
            var nameWithoutGeneric = GetNameWithoutGeneric(typeName);
            var collectionKind = typeSymbol.CollectionKind;

            // Check if this is a built-in type or collection of built-in types
            var isBuiltInTypeOrBuiltInCollection = ((ITypeSymbol)typeSymbol).IsBuiltInTypeOrBuiltInCollection;

            return new TypeData(
                typeName,
                nameWithoutGeneric,
                typeSymbol.ContainsGenericParameters,
                typeSymbol.Arity,
                typeSymbol.IsNestedOpenGeneric,
                IsTypeParameter: false, // Named types are not type parameters
                collectionKind,
                isBuiltInTypeOrBuiltInCollection,
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

            // Check if this is a collection type
            var collectionKind = typeSymbol.CollectionKind;

            // Check if this is a built-in type or collection of built-in types
            var isBuiltInTypeOrBuiltInCollection = ((ITypeSymbol)typeSymbol).IsBuiltInTypeOrBuiltInCollection;

            return new TypeData(
                typeName,
                nameWithoutGeneric,
                typeSymbol.ContainsGenericParameters,
                typeSymbol.Arity,
                typeSymbol.IsNestedOpenGeneric,
                IsTypeParameter: false, // Named types are not type parameters
                collectionKind,
                isBuiltInTypeOrBuiltInCollection,
                typeParameters);
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
                var (serviceKey, hasInjectAttribute, hasServiceKeyAttribute, hasFromKeyedServicesAttribute) = param.GetServiceKeyAndAttributeInfo();

                // Get the C# code representation of the default value
                var defaultValue = hasDefaultValue ? ToDefaultValueCodeString(param.ExplicitDefaultValue) : null;

                parameters.Add(new ParameterData(param.Name, paramTypeData,
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

        /// <summary>
        /// Gets the CollectionKind for the given type symbol using the type and its implemented interfaces.
        /// Priority: Set > MutableCollection > ReadOnlyCollection > Enumerable > None.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to check.</param>
        /// <returns>The CollectionKind based on the type and its interfaces.</returns>
        public CollectionKind CollectionKind
        {
            get
            {
                var nameWithoutGeneric = GetNameWithoutGeneric(typeSymbol.FullyQualifiedName);

                if(IsReadOnlyCollectionType(nameWithoutGeneric) || typeSymbol.TypeKind == TypeKind.Array)
                    return CollectionKind.ReadOnlyCollection;

                if(IsEnumerableType(nameWithoutGeneric))
                    return CollectionKind.Enumerable;

                return CollectionKind.None;
            }
        }

        public bool IsInject =>
            typeSymbol.Name is "IocInjectAttribute" or "InjectAttribute";
    }

    extension(IParameterSymbol param)
    {
        /// <summary>
        /// Gets the service key, injection attribute info, and [ServiceKey]/[FromKeyedServices] attribute from a parameter.
        /// [FromKeyedServices] takes precedence over [Inject] for service key resolution.
        /// HasInjectAttribute is only true for [Inject] attribute (not [FromKeyedServices], which MS.DI handles automatically).
        /// HasServiceKeyAttribute indicates the parameter is marked with [ServiceKey] from Microsoft.Extensions.DependencyInjection.
        /// HasFromKeyedServicesAttribute indicates the parameter is marked with [FromKeyedServices] from Microsoft.Extensions.DependencyInjection.
        /// </summary>
        /// <returns>A tuple containing the service key (if any), whether the parameter has [Inject] attribute, [ServiceKey] attribute, and [FromKeyedServices] attribute.</returns>
        public (string? ServiceKey, bool HasInjectAttribute, bool HasServiceKeyAttribute, bool HasFromKeyedServicesAttribute) GetServiceKeyAndAttributeInfo()
        {
            string? serviceKey = null;
            bool hasInjectAttribute = false;
            bool hasServiceKeyAttribute = false;
            bool hasFromKeyedServicesAttribute = false;

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
                    hasFromKeyedServicesAttribute = true;
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

                // Check for IocInjectAttribute/InjectAttribute (by name only, to support third-party attributes)
                if(attrClass.IsInject)
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
            return (serviceKey, hasInjectAttribute, hasServiceKeyAttribute, hasFromKeyedServicesAttribute);
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

    extension(IArrayTypeSymbol arrayTypeSymbol)
    {
        public TypeData GetTypeData(
            bool extractConstructorParams = false,
            bool extractHierarchy = false,
            HashSet<INamedTypeSymbol>? visited = null)
        {
            var elementType = arrayTypeSymbol.ElementType;
            var typeName = arrayTypeSymbol.FullyQualifiedName;

            // Check if this is a built-in type array
            var isBuiltInTypeOrBuiltInCollection = elementType.IsBuiltInType;

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
                CollectionKind: CollectionKind.ReadOnlyCollection, // Arrays are read-only collections
                IsBuiltInTypeOrBuiltInCollection: isBuiltInTypeOrBuiltInCollection,
                TypeParameters: typeParameters);
        }
    }

    extension(AttributeData attribute)
    {
        /// <summary>
        /// Gets an array of type symbols from a named argument.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetTypeArrayArgument(string name, bool extractConstructorParams = false)
        {
            foreach(var namedArg in attribute.NamedArguments)
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
            foreach(var ctorArg in attribute.ConstructorArguments)
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

        public (bool HasArg, ServiceLifetime Lifetime) TryGetLifetime()
        {
            // First, check if lifetime is passed as a constructor argument (for generic attributes like IoCRegisterAttribute<T>(ServiceLifetime.Scoped))
            foreach(var ctorArg in attribute.ConstructorArguments)
            {
                if(ctorArg.Type?.Name == nameof(ServiceLifetime) && ctorArg.Value is int lifetimeValue)
                {
                    return (true, (ServiceLifetime)lifetimeValue);
                }
            }

            // Fall back to named argument
            var (hasArg, val) = attribute.TryGetNamedArgument<int>("Lifetime", 0); // Default is ServiceLifetime.Singleton
            return (hasArg, (ServiceLifetime)val);
        }

        public (bool HasArg, bool Value) TryGetRegisterAllInterfaces() =>
            attribute.TryGetNamedArgument<bool>("RegisterAllInterfaces", false);

        public (bool HasArg, bool Value) TryGetRegisterAllBaseClasses() =>
            attribute.TryGetNamedArgument<bool>("RegisterAllBaseClasses", false);

        /// <summary>
        /// Gets the service types from the attribute.
        /// This method checks both named arguments and constructor arguments for service types.
        /// </summary>
        /// <remarks>
        /// The method first checks for a named argument "ServiceTypes" (e.g., ServiceTypes = [typeof(IService)]).
        /// If not found, it checks constructor arguments for an array of types (e.g., params Type[] serviceTypes).
        /// </remarks>
        public ImmutableEquatableArray<TypeData> GetServiceTypes()
        {
            // First, try to get from named argument
            var namedResult = attribute.GetTypeArrayArgument("ServiceTypes");
            if(namedResult.Length > 0)
                return namedResult;

            // Fall back to constructor argument (params Type[] serviceTypes)
            return attribute.GetTypeArrayFromConstructorArgument();
        }

        /// <summary>
        /// Gets the service types from generic attribute type parameters (e.g., IoCRegisterAttribute&lt;T1, T2&gt;).
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetServiceTypesFromGenericAttribute()
        {
            var attrClass = attribute.AttributeClass;
            if(attrClass?.IsGenericType != true || attrClass.TypeArguments.Length == 0)
                return [];

            List<TypeData> result = [];
            foreach(var typeArg in attrClass.TypeArguments)
            {
                if(typeArg is INamedTypeSymbol namedType)
                {
                    result.Add(namedType.GetTypeData());
                }
            }
            return result.ToImmutableEquatableArray();
        }

        public ImmutableEquatableArray<TypeData> GetDecorators() =>
            attribute.GetTypeArrayArgument("Decorators", extractConstructorParams: true);

        /// <summary>
        /// Gets the Tags array from the attribute.
        /// </summary>
        public ImmutableEquatableArray<string> GetTags()
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key.Equals("Tags", StringComparison.Ordinal) && !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    List<string> result = [];
                    foreach(var value in namedArg.Value.Values)
                    {
                        if(value.Value is string tag)
                        {
                            result.Add(tag);
                        }
                    }
                    return result.ToImmutableEquatableArray();
                }
            }

            return [];
        }

        /// <summary>
        /// Gets the TagOnly value from the attribute.
        /// </summary>
        public bool GetTagOnly() =>
            attribute.GetNamedArgument<bool>("TagOnly", false);

        /// <summary>
        /// Gets the Key information from the attribute (IoCRegisterAttribute or IoCRegisterForAttribute).
        /// </summary>
        /// <returns>
        /// A tuple containing:
        /// - HasKey: True if a Key is specified (regardless of KeyType)
        /// - KeyTypeSymbol: The type symbol of the key value, or null if no key is specified or KeyType is Csharp
        /// </returns>
        public (bool HasKey, ITypeSymbol? KeyTypeSymbol) GetKeySymbol()
        {
            // When KeyType is Csharp (1), the key is a C# expression string, not a typed value
            var keyType = attribute.GetNamedArgument<int>("KeyType", 0);
            var isCsharpKeyType = keyType == 1;

            // Check named arguments
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key == "Key" && !namedArg.Value.IsNull)
                {
                    // Has key from named argument, but type is null if KeyType is Csharp
                    return (true, isCsharpKeyType ? null : namedArg.Value.Type);
                }
            }

            return (false, null);
        }

        /// <summary>
        /// Gets the Key and KeyType from the attribute.
        /// </summary>
        /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
        /// <returns>A tuple containing the key string and key type.</returns>
        public (string? Key, int KeyType) GetKey(SemanticModel? semanticModel = null)
        {
            var keyType = attribute.GetNamedArgument<int>("KeyType", 0);
            string? key = null;

            // First, check if key is passed as a constructor argument (e.g., InjectAttribute(object key))
            if(attribute.ConstructorArguments.Length > 0)
            {
                var ctorArg = attribute.ConstructorArguments[0];
                // Skip if the first argument is a type, lifetime enum, or array (e.g., IoCRegisterDefaultsAttribute)
                if(ctorArg.Type?.Name != nameof(ServiceLifetime)
                    && ctorArg.Kind != TypedConstantKind.Type
                    && ctorArg.Kind != TypedConstantKind.Array)
                {
                    if(!ctorArg.IsNull)
                    {
                        if(keyType == 1) // KeyType.Csharp
                        {
                            // Try to get original syntax for nameof() expressions with full access path resolution
                            key = attribute.TryGetNameofFromConstructorArg(0, semanticModel)
                                ?? ctorArg.Value?.ToString();
                        }
                        else
                        {
                            key = ctorArg.GetPrimitiveConstantString();
                            keyType = 1; // Treat as CSharp code
                        }

                        return (key, keyType);
                    }
                }
            }

            // Fall back to named argument
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key == "Key")
                {
                    if(namedArg.Value.IsNull)
                    {
                        key = null;
                    }
                    else
                    {
                        if(keyType == 1) // KeyType.Csharp
                        {
                            // Try to get original syntax for nameof() expressions with full access path resolution
                            key = attribute.TryGetNameof("Key", semanticModel)
                                ?? namedArg.Value.Value?.ToString();
                        }
                        else
                        {
                            key = namedArg.Value.GetPrimitiveConstantString();
                            keyType = 1; // Treat as CSharp code
                        }
                    }
                    break;
                }
            }

            return (key, keyType);
        }

        /// <summary>
        /// Gets the Factory method data from the attribute, including parameter and return type information.
        /// </summary>
        /// <param name="semanticModel">Semantic model to resolve method symbols.</param>
        /// <returns>The factory method data, or null if not specified.</returns>
        public FactoryMethodData? GetFactoryMethodData(SemanticModel semanticModel)
        {
            var syntaxReference = attribute.ApplicationSyntaxReference;
            if(syntaxReference?.GetSyntax() is not AttributeSyntax attributeSyntax)
                return null;

            var argumentList = attributeSyntax.ArgumentList;
            if(argumentList is null)
                return null;

            foreach(var argument in argumentList.Arguments)
            {
                if(argument.NameEquals?.Name.Identifier.Text != "Factory")
                    continue;

                // Check if the expression is a nameof() invocation
                if(argument.Expression is InvocationExpressionSyntax invocation &&
                   invocation.Expression is IdentifierNameSyntax identifierName &&
                   identifierName.Identifier.Text == "nameof" &&
                   invocation.ArgumentList.Arguments.Count == 1)
                {
                    var nameofArgument = invocation.ArgumentList.Arguments[0].Expression;
                    var methodSymbol = ResolveMethodSymbol(nameofArgument, semanticModel);

                    if(methodSymbol is not null)
                    {
                        return CreateFactoryMethodData(methodSymbol);
                    }

                    // Fallback: get path from nameof expression
                    var nameofPath = ResolveNameofExpression(nameofArgument, semanticModel)
                                     ?? nameofArgument.ToFullString().Trim();
                    return new FactoryMethodData(nameofPath, HasServiceProvider: true, HasKey: false, ReturnTypeName: null);
                }

                // String literal - cannot determine parameters, assume full signature
                if(argument.Expression is LiteralExpressionSyntax literal &&
                   literal.Token.Value is string literalPath)
                {
                    return new FactoryMethodData(literalPath, HasServiceProvider: true, HasKey: false, ReturnTypeName: null);
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the Instance path from the attribute.
        /// </summary>
        /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
        /// <returns>The static instance path (e.g., "MyService.Default"), or null if not specified.</returns>
        public string? GetInstance(SemanticModel? semanticModel = null)
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key == "Instance")
                {
                    if(namedArg.Value.IsNull)
                        return null;

                    // Try to get original syntax for nameof() expressions with full access path resolution
                    return attribute.TryGetNameof("Instance", semanticModel)
                        ?? namedArg.Value.Value?.ToString();
                }
            }

            return null;
        }

        /// <summary>
        /// Determines if the attribute will cause registration of interfaces or base classes.
        /// For open generic types, nested open generics are only a problem when registering interfaces/base classes.
        /// </summary>
        public bool WillRegisterInterfacesOrBaseClasses()
        {
            // Check if ServiceTypes is specified
            var serviceTypes = attribute.GetServiceTypes();
            if(serviceTypes.Length > 0)
                return true;

            // Check if RegisterAllInterfaces is true
            var (hasRegisterAllInterfaces, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
            if(hasRegisterAllInterfaces && registerAllInterfaces)
                return true;

            // Check if RegisterAllBaseClasses is true
            var (hasRegisterAllBaseClasses, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
            if(hasRegisterAllBaseClasses && registerAllBaseClasses)
                return true;

            // Only registering self, no interfaces/base classes
            return false;
        }

        /// <summary>
        /// Extracts default settings from an IoCRegisterDefaultSettingsAttribute.
        /// </summary>
        /// <returns>The default settings model, or null if the attribute data is invalid.</returns>
        public DefaultSettingsModel? ExtractDefaultSettings()
        {
            if(attribute.ConstructorArguments.Length < 2)
                return null;
            if(attribute.ConstructorArguments[0].Value is not INamedTypeSymbol targetServiceType)
                return null;
            if(attribute.ConstructorArguments[1].Value is not int lifetime)
                return null;

            var (_, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
            var (_, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
            var serviceTypes = attribute.GetServiceTypes();
            var typeData = targetServiceType.GetTypeData();
            var decorators = attribute.GetDecorators();
            var tags = attribute.GetTags();
            var tagOnly = attribute.GetTagOnly();

            return new DefaultSettingsModel(
                typeData,
                (ServiceLifetime)lifetime,
                registerAllInterfaces,
                registerAllBaseClasses,
                serviceTypes,
                decorators,
                tags,
                tagOnly);
        }

        /// <summary>
        /// Extracts default settings from a generic IoCRegisterDefaultsAttribute (e.g., IoCRegisterDefaultsAttribute&lt;T&gt;).
        /// The target service type is specified via type parameter instead of constructor argument.
        /// </summary>
        /// <returns>The default settings model, or null if the attribute data is invalid.</returns>
        public DefaultSettingsModel? ExtractDefaultSettingsFromGenericAttribute()
        {
            var attrClass = attribute.AttributeClass;
            if(attrClass?.IsGenericType != true || attrClass.TypeArguments.Length == 0)
                return null;

            if(attrClass.TypeArguments[0] is not INamedTypeSymbol targetServiceType)
                return null;

            // Lifetime is the first constructor argument for the generic version
            if(attribute.ConstructorArguments.Length < 1)
                return null;
            if(attribute.ConstructorArguments[0].Value is not int lifetime)
                return null;

            var (_, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
            var (_, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
            var serviceTypes = attribute.GetServiceTypes();
            var typeData = targetServiceType.GetTypeData();
            var decorators = attribute.GetDecorators();
            var tags = attribute.GetTags();
            var tagOnly = attribute.GetTagOnly();

            return new DefaultSettingsModel(
                typeData,
                (ServiceLifetime)lifetime,
                registerAllInterfaces,
                registerAllBaseClasses,
                serviceTypes,
                decorators,
                tags,
                tagOnly);
        }
    }

    extension(IMethodSymbol methodSymbol)
    {
        /// <summary>
        /// Creates FactoryMethodData from a method symbol.
        /// </summary>
        public FactoryMethodData CreateFactoryMethodData()
        {
            var path = methodSymbol.FullAccessPath;
            bool hasServiceProvider = false;
            bool hasKey = false;

            foreach(var param in methodSymbol.Parameters)
            {
                var paramTypeName = param.Type.FullyQualifiedName;

                // Check for IServiceProvider
                if(paramTypeName is "global::System.IServiceProvider" or "System.IServiceProvider")
                {
                    hasServiceProvider = true;
                }
            }

            // Always store the return type for runtime comparison
            var returnTypeName = methodSymbol.ReturnType.FullyQualifiedName;

            return new FactoryMethodData(path, hasServiceProvider, hasKey, returnTypeName);
        }
    }

    /// <param name="typeData">The type data to check.</param>
    extension(TypeData typeData)
    {
        /// <summary>
        /// Checks if the type is an array type (e.g., T[]).
        /// </summary>
        public bool IsArrayType =>
            typeData.Name.EndsWith("[]", StringComparison.Ordinal);

        /// <summary>
        /// Tries to extract the element type from an enumerable-compatible type.
        /// Handles IEnumerable&lt;T&gt;, IList&lt;T&gt;, ICollection&lt;T&gt;, List&lt;T&gt;, HashSet&lt;T&gt;, T[], etc.
        /// </summary>
        /// <param name="checkInterfaces">
        /// When true, also checks AllInterfaces for IEnumerable&lt;T&gt; implementation.
        /// Use true for closed generic dependency extraction, false for DI injection handling.
        /// </param>
        /// <returns>The element type if this is an enumerable-compatible type; otherwise, null.</returns>
        public TypeData? TryGetElementType(bool checkInterfaces = false)
        {
            // Handle arrays first
            if(typeData.IsArrayType)
            {
                var typeParams = typeData.TypeParameters;
                return typeParams is { Length: 1 } ? typeParams[0].Type : null;
            }

            // Check if this type itself is an enumerable-compatible collection type
            if(typeData.GenericArity == 1)
            {
                // For DI injection: only handle types with CollectionKind (IEnumerable<T>, IList<T>, etc.)
                // For dependency extraction: handle any type with IsEnumerableType name pattern
                var isEnumerable = checkInterfaces
                    ? IsEnumerableType(typeData.NameWithoutGeneric)
                    : typeData.CollectionKind is not CollectionKind.None;

                if(isEnumerable)
                {
                    var typeParams = typeData.TypeParameters;
                    if(typeParams is { Length: 1 })
                    {
                        return typeParams[0].Type;
                    }
                }
            }

            // Check AllInterfaces for IEnumerable<T> implementation (only when checkInterfaces is true)
            if(checkInterfaces && typeData.AllInterfaces is { Length: > 0 })
            {
                foreach(var iface in typeData.AllInterfaces)
                {
                    // Look for IEnumerable<T> (with exactly one type argument)
                    if(IsEnumerableType(iface.NameWithoutGeneric) && iface.GenericArity == 1)
                    {
                        var ifaceTypeParams = iface.TypeParameters;
                        if(ifaceTypeParams is { Length: 1 })
                        {
                            return ifaceTypeParams[0].Type;
                        }
                    }
                }
            }

            return null;
        }
    }
}
