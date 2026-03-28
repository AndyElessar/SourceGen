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
            return TypeData.CreateSimple(name);
        }
    }

    extension(INamedTypeSymbol typeSymbol)
    {
        /// <summary>
        /// Gets the type data for this type symbol.
        /// </summary>
        /// <param name="extractConstructorParams">Whether to extract constructor parameters recursively.</param>
        /// <param name="extractHierarchy">Whether to extract all interfaces and base classes.</param>
        /// <param name="visited">Set of visited types to prevent infinite recursion during constructor parameter extraction.</param>
        /// <param name="semanticModel">Optional semantic model for resolving nameof() expressions. Only used for top-level extraction, not passed to recursive calls.</param>
        /// <param name="extractInjectionMembers">Whether to extract injection members (properties, fields, methods with [IocInject] attributes). Used for decorators.</param>
        public TypeData GetTypeData(
            bool extractConstructorParams = false,
            bool extractHierarchy = false,
            HashSet<INamedTypeSymbol>? visited = null,
            SemanticModel? semanticModel = null,
            bool extractInjectionMembers = false)
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
                // Pass semanticModel only for top-level extraction
                // Recursive calls from within ExtractConstructorParametersWithInfo do not receive semanticModel
                // to avoid cross-compilation-unit issues and stack overflow
                (constructorParams, hasInjectConstructor) = typeSymbol.ExtractConstructorParametersWithInfo(visited, semanticModel);
            }

            // Extract injection members if requested (for decorators)
            ImmutableEquatableArray<InjectionMemberData>? injectionMembers = null;
            if(extractInjectionMembers)
            {
                injectionMembers = typeSymbol.ExtractInjectionMembersForDecorator(semanticModel);
                if(injectionMembers.Length == 0)
                {
                    injectionMembers = null;
                }
            }

            // Extract hierarchy (interfaces and base classes) if requested
            ImmutableEquatableArray<TypeData>? allInterfaces = null;
            ImmutableEquatableArray<TypeData>? allBaseClasses = null;
            if(extractHierarchy)
            {
                allInterfaces = typeSymbol.GetAllInterfaces();
                allBaseClasses = typeSymbol.GetAllBaseClasses();
            }

            // Error types (e.g., types from other source generators not yet resolved)
            // should be treated as simple types, not open generics
            if(typeSymbol.TypeKind == TypeKind.Error)
            {
                return TypeData.CreateSimple(
                    typeName,
                    constructorParams,
                    hasInjectConstructor,
                    injectionMembers,
                    allInterfaces,
                    allBaseClasses);
            }

            // Check if this is a wrapper type (collection or non-collection) for DI
            var nameWithoutGeneric = GetNameWithoutGeneric(typeName);
            var wrapperKind = typeSymbol.GetWrapperKind(nameWithoutGeneric);

            if(wrapperKind is not WrapperKind.None)
            {
                return TypeData.CreateWrapper(
                    typeName,
                    nameWithoutGeneric,
                    typeSymbol.ContainsGenericParameters,
                    typeSymbol.Arity,
                    wrapperKind,
                    typeSymbol.IsNestedOpenGeneric,
                    typeParameters,
                    constructorParams,
                    hasInjectConstructor,
                    injectionMembers,
                    allInterfaces,
                    allBaseClasses);
            }

            if(typeSymbol.ContainsGenericParameters || typeSymbol.Arity > 0 || typeParameters is { Length: > 0 })
            {
                return TypeData.CreateGeneric(
                    typeName,
                    nameWithoutGeneric,
                    typeSymbol.ContainsGenericParameters,
                    typeSymbol.Arity,
                    typeSymbol.IsNestedOpenGeneric,
                    typeParameters,
                    constructorParams,
                    hasInjectConstructor,
                    injectionMembers,
                    allInterfaces,
                    allBaseClasses);
            }

            return TypeData.CreateSimple(
                typeName,
                constructorParams,
                hasInjectConstructor,
                injectionMembers,
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
                            : ct.TypeKind == TypeKind.TypeParameter
                                ? TypeData.CreateTypeParameter(ct.FullyQualifiedName)
                                : TypeData.CreateGeneric(
                                    ct.FullyQualifiedName,
                                    GetNameWithoutGeneric(ct.FullyQualifiedName),
                                    ct.ContainsGenericParameters,
                                    0,
                                    false))
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

            // Check if this is a wrapper type (collection or non-collection)
            var wrapperKind = typeSymbol.GetWrapperKind(nameWithoutGeneric);

            if(wrapperKind is not WrapperKind.None)
            {
                return TypeData.CreateWrapper(
                    typeName,
                    nameWithoutGeneric,
                    typeSymbol.ContainsGenericParameters,
                    typeSymbol.Arity,
                    wrapperKind,
                    typeSymbol.IsNestedOpenGeneric,
                    typeParameters);
            }

            if(typeSymbol.ContainsGenericParameters || typeSymbol.Arity > 0 || typeParameters is { Length: > 0 })
            {
                return TypeData.CreateGeneric(
                    typeName,
                    nameWithoutGeneric,
                    typeSymbol.ContainsGenericParameters,
                    typeSymbol.Arity,
                    typeSymbol.IsNestedOpenGeneric,
                    typeParameters);
            }

                    return TypeData.CreateSimple(typeName);
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
        /// Gets the <see cref="WrapperKind"/> for the given type symbol.
        /// Checks collection types first, then non-collection wrapper types.
        /// </summary>
        /// <param name="nameWithoutGeneric">The type name without generic parameters.</param>
        /// <returns>The <see cref="WrapperKind"/> for this type.</returns>
        public WrapperKind GetWrapperKind(string nameWithoutGeneric)
        {
            if(typeSymbol.TypeKind == TypeKind.Array)
                return WrapperKind.Array;

            if(IsReadOnlyCollectionType(nameWithoutGeneric))
                return WrapperKind.ReadOnlyCollection;

            if(IsReadOnlyListType(nameWithoutGeneric))
                return WrapperKind.ReadOnlyList;

            if(IsCollectionType(nameWithoutGeneric))
                return WrapperKind.Collection;

            if(IsListType(nameWithoutGeneric))
                return WrapperKind.List;

            if(IsEnumerableType(nameWithoutGeneric))
                return WrapperKind.Enumerable;

            return GetNonCollectionWrapperKind(nameWithoutGeneric);
        }

        /// <summary>
        /// Determines the <see cref="WrapperKind"/> for a given type name (without generic part).
        /// Returns <see cref="WrapperKind.None"/> if the type is not a recognized non-collection wrapper type.
        /// Collection types (IEnumerable, IReadOnlyCollection, etc.) are detected separately
        /// in <see cref="TransformExtensions"/> via <c>GetWrapperKind</c>.
        /// </summary>
        public static WrapperKind GetNonCollectionWrapperKind(string nameWithoutGeneric) => nameWithoutGeneric switch
        {
            "global::System.Lazy" or "System.Lazy" or "Lazy" => WrapperKind.Lazy,
            "global::System.Func" or "System.Func" or "Func" => WrapperKind.Func,
            "global::System.Collections.Generic.IDictionary" or "System.Collections.Generic.IDictionary" or "IDictionary"
                or "global::System.Collections.Generic.IReadOnlyDictionary" or "System.Collections.Generic.IReadOnlyDictionary" or "IReadOnlyDictionary"
                or "global::System.Collections.Generic.Dictionary" or "System.Collections.Generic.Dictionary" or "Dictionary"
                => WrapperKind.Dictionary,
            "global::System.Collections.Generic.KeyValuePair" or "System.Collections.Generic.KeyValuePair" or "KeyValuePair" => WrapperKind.KeyValuePair,
            "global::System.Threading.Tasks.Task" or "System.Threading.Tasks.Task" or "Task" => WrapperKind.Task,
            _ => WrapperKind.None
        };

        public bool IsInject =>
            typeSymbol.Name is "IocInjectAttribute" or "InjectAttribute";

        /// <summary>
        /// Enumerates members (properties, fields, methods) marked with IocInjectAttribute/InjectAttribute.
        /// This is a shared method used by both Analyzer (ServiceInfo) and Generator (RegistrationData).
        /// </summary>
        /// <remarks>
        /// The method filters members based on:
        /// - Non-static members only
        /// - Properties with a setter
        /// - Non-readonly fields
        /// - Ordinary methods that return <see langword="void"/> (sync) or non-generic
        ///   <see cref="System.Threading.Tasks.Task"/> (async, when <c>AsyncMethodInject</c>
        ///   feature is enabled), and are not generic
        /// </remarks>
        /// <returns>
        /// An enumerable of tuples containing the member symbol and its inject attribute.
        /// Analyzer can use ISymbol directly; Generator can convert to InjectionMemberData.
        /// </returns>
        public IEnumerable<(ISymbol Member, AttributeData InjectAttribute)> GetInjectedMembers()
        {
            // For unbound generic types (e.g., LoggingDecorator<,>), we need to use OriginalDefinition
            // to get the actual member declarations with their attributes
            var typeToInspect = typeSymbol.IsUnboundGenericType ? typeSymbol.OriginalDefinition : typeSymbol;

            foreach(var member in typeToInspect.GetMembers())
            {
                // Skip static members
                if(member.IsStatic)
                    continue;

                // Check if the member has IocInjectAttribute/InjectAttribute (by name only)
                var injectAttribute = member.GetAttributes()
                    .FirstOrDefault(static attr => attr.AttributeClass?.IsInject == true);

                if(injectAttribute is null)
                    continue;

                // Validate member is injectable based on type
                var isInjectable = member switch
                {
                    IPropertySymbol property => property.SetMethod is not null,
                    IFieldSymbol field => !field.IsReadOnly,
                    IMethodSymbol method => method.MethodKind == MethodKind.Ordinary
                        && (method.ReturnsVoid || RoslynExtensions.IsNonGenericTaskReturnType(method))
                        && !method.IsGenericMethod,
                    _ => false
                };

                if(isInjectable)
                {
                    yield return (member, injectAttribute);
                }
            }
        }
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
        public (string? ServiceKey, bool HasInjectAttribute, bool HasServiceKeyAttribute, bool HasFromKeyedServicesAttribute) GetServiceKeyAndAttributeInfo(SemanticModel? semanticModel = null)
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
                        var (key, _, _) = attribute.GetKeyInfo(semanticModel);
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
                TypeData typeData = typeArg.TypeKind == TypeKind.TypeParameter
                    ? TypeData.CreateTypeParameter(argName)
                    : TypeData.CreateGeneric(
                        argName,
                        GetNameWithoutGeneric(argName),
                        typeArg.ContainsGenericParameters,
                        0);
                return (typeData, null);
            }

            // No type argument available, this is a type parameter placeholder
            return (TypeData.CreateTypeParameter(typeParam.Name), null);

            // Creates a simple TypeData for an interface type.
            static TypeData CreateInterfaceTypeData(INamedTypeSymbol iface) =>
                iface.IsGenericType || iface.Arity > 0
                    ? TypeData.CreateGeneric(
                        iface.FullyQualifiedName,
                        GetNameWithoutGeneric(iface.FullyQualifiedName),
                        iface.ContainsGenericParameters,
                        iface.Arity,
                        false)
                    : TypeData.CreateSimple(iface.FullyQualifiedName);
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
                TypeData elementTypeData = elementType.TypeKind == TypeKind.TypeParameter
                    ? TypeData.CreateTypeParameter(elementTypeName)
                    : TypeData.CreateGeneric(
                        elementTypeName,
                        GetNameWithoutGeneric(elementTypeName),
                        elementType.ContainsGenericParameters,
                        0);
                typeParameters = [new TypeParameter("T", elementTypeData)];
            }

            return TypeData.CreateWrapper(
                typeName,
                typeName, // For arrays, use full name as NameWithoutGeneric
                elementType.ContainsGenericParameters,
                GenericArity: 1, // Arrays have one "type parameter" (the element type)
                WrapperKind.Array, // Arrays are collections
                IsNestedOpenGeneric: false,
                TypeParameters: typeParameters);
        }
    }

    extension(AttributeData attribute)
    {
        /// <summary>
        /// Gets an array of type symbols from a named argument.
        /// </summary>
        /// <param name="name">The name of the named argument.</param>
        /// <param name="extractConstructorParams">Whether to extract constructor parameters.</param>
        /// <param name="extractInjectionMembers">Whether to extract injection members (for decorators).</param>
        public ImmutableEquatableArray<TypeData> GetTypeArrayArgument(
            string name,
            bool extractConstructorParams = false,
            bool extractInjectionMembers = false)
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key.Equals(name, StringComparison.Ordinal) && !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    List<TypeData> result = [];
                    foreach(var value in namedArg.Value.Values)
                    {
                        if(value.Value is INamedTypeSymbol namedTypeSymbol)
                        {
                            result.Add(namedTypeSymbol.GetTypeData(
                                extractConstructorParams,
                                extractHierarchy: false,
                                visited: null,
                                semanticModel: null,
                                extractInjectionMembers));
                        }
                        else if(value.Value is ITypeSymbol typeSymbol)
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
            var (hasArg, val) = attribute.TryGetNamedArgument<int>("Lifetime", 2); // Default is ServiceLifetime.Transient
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
            attribute.GetTypeArrayArgument("Decorators", extractConstructorParams: true, extractInjectionMembers: true);

        /// <summary>
        /// Gets the ImplementationTypes array from the attribute.
        /// Extracts implementation types with constructor parameters and hierarchy information,
        /// using the same parsing logic as IocRegisterAttribute.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetImplementationTypes() =>
            attribute.GetTypeArrayArgumentWithHierarchy("ImplementationTypes");

        /// <summary>
        /// Gets the ImplementationTypes array as INamedTypeSymbol from the attribute.
        /// Used when full symbol access is needed for injection member extraction.
        /// </summary>
        public ImmutableEquatableArray<INamedTypeSymbol> GetImplementationTypeSymbols() =>
            attribute.GetTypeSymbolsFromNamedArgument("ImplementationTypes");

        /// <summary>
        /// Gets an array of type symbols from a named argument.
        /// Used when full symbol access is needed for further analysis.
        /// </summary>
        public ImmutableEquatableArray<INamedTypeSymbol> GetTypeSymbolsFromNamedArgument(string name)
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key.Equals(name, StringComparison.Ordinal) && !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    List<INamedTypeSymbol> result = [];
                    foreach(var value in namedArg.Value.Values)
                    {
                        if(value.Value is INamedTypeSymbol namedTypeSymbol)
                        {
                            result.Add(namedTypeSymbol);
                        }
                    }
                    return result.ToImmutableEquatableArray();
                }
            }

            return [];
        }

        /// <summary>
        /// Gets an array of type symbols from a named argument with full hierarchy extraction.
        /// Used for ImplementationTypes where we need constructor params and all interfaces/base classes.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetTypeArrayArgumentWithHierarchy(string name)
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key.Equals(name, StringComparison.Ordinal) && !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    List<TypeData> result = [];
                    foreach(var value in namedArg.Value.Values)
                    {
                        if(value.Value is INamedTypeSymbol namedTypeSymbol)
                        {
                            // Extract with constructor params and hierarchy, same as IocRegisterAttribute
                            result.Add(namedTypeSymbol.GetTypeData(extractConstructorParams: true, extractHierarchy: true));
                        }
                    }
                    return result.ToImmutableEquatableArray();
                }
            }

            return [];
        }

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
        /// Checks if the attribute has Factory or Instance specified.
        /// </summary>
        /// <returns>A tuple indicating whether Factory and/or Instance are specified.</returns>
        public (bool HasFactory, bool HasInstance) HasFactoryOrInstance()
        {
            bool hasFactory = false;
            bool hasInstance = false;

            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key == "Factory" && !namedArg.Value.IsNull)
                {
                    hasFactory = true;
                }
                else if(namedArg.Key == "Instance" && !namedArg.Value.IsNull)
                {
                    hasInstance = true;
                }

                // Early exit if both found
                if(hasFactory && hasInstance)
                    break;
            }

            return (hasFactory, hasInstance);
        }

        /// <summary>
        /// Gets the target type from an IoCRegisterForAttribute.
        /// For non-generic variant, extracts from constructor argument.
        /// For generic variant (IoCRegisterForAttribute&lt;T&gt;), extracts from type parameter.
        /// </summary>
        /// <returns>The target type symbol, or null if not found.</returns>
        public INamedTypeSymbol? GetTargetTypeFromRegisterForAttribute()
        {
            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                return null;

            // For generic IoCRegisterForAttribute<T>, get T from type arguments
            if(attributeClass.IsGenericType && attributeClass.TypeArguments.Length > 0)
            {
                return attributeClass.TypeArguments[0] as INamedTypeSymbol;
            }

            // For non-generic IoCRegisterForAttribute, get from constructor argument
            if(attribute.ConstructorArguments.Length > 0 &&
               attribute.ConstructorArguments[0].Value is INamedTypeSymbol targetType)
            {
                return targetType;
            }

            return null;
        }

        /// <summary>
        /// Gets all key-related information from the attribute in a single pass:
        /// key string, key type, and key value type symbol (with optional nameof() resolution).
        /// </summary>
        /// <param name="semanticModel">Optional semantic model to resolve nameof() expression types and full access paths.</param>
        /// <returns>
        /// A tuple containing:
        /// - Key: The key string, or null if no key is specified.
        /// - KeyType: The key type (0 = Value, 1 = Csharp).
        /// - KeyValueTypeSymbol: The type symbol of the key value, or null when the type cannot be determined.
        /// </returns>
        public (string? Key, int KeyType, ITypeSymbol? KeyValueTypeSymbol) GetKeyInfo(SemanticModel? semanticModel = null)
        {
            var keyType = attribute.GetNamedArgument<int>("KeyType", 0);
            var isCsharpKeyType = keyType == 1;

            // First, check if key is passed as a constructor argument (e.g., InjectAttribute(object key))
            if(attribute.ConstructorArguments.Length > 0)
            {
                var ctorArg = attribute.ConstructorArguments[0];
                // Skip if the first argument is a type, lifetime enum, or array (e.g., IoCRegisterDefaultsAttribute)
                if(ctorArg.Type?.Name != nameof(ServiceLifetime)
                    && ctorArg.Kind != TypedConstantKind.Type
                    && ctorArg.Kind != TypedConstantKind.Array
                    && !ctorArg.IsNull)
                {
                    if(isCsharpKeyType)
                    {
                        // Try to get original syntax for nameof() expressions with full access path resolution
                        var key = attribute.TryGetNameofFromConstructorArg(0, semanticModel)
                            ?? ctorArg.Value?.ToString();
                        var keyValueType = TryResolveNameofTypeFromConstructorArg(attribute, 0, semanticModel);
                        return (key, keyType, keyValueType);
                    }

                    // Value key: treat the primitive constant as CSharp code
                    return (ctorArg.GetPrimitiveConstantString(), 1, ctorArg.Type);
                }
            }

            // Fall back to named argument
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key != "Key")
                    continue;

                if(namedArg.Value.IsNull)
                    return (null, keyType, null);

                if(isCsharpKeyType)
                {
                    // Try to get original syntax for nameof() expressions with full access path resolution
                    var key = attribute.TryGetNameof("Key", semanticModel)
                        ?? namedArg.Value.Value?.ToString();
                    var keyValueType = TryResolveNameofTypeFromNamedArg(attribute, "Key", semanticModel);
                    return (key, keyType, keyValueType);
                }

                // Value key: treat the primitive constant as CSharp code
                return (namedArg.Value.GetPrimitiveConstantString(), 1, namedArg.Value.Type);
            }

            return (null, keyType, null);
        }

        /// <summary>
        /// Tries to resolve the type of a nameof() expression in a constructor argument.
        /// Returns null if the argument is not a nameof() expression or cannot be resolved.
        /// </summary>
        private static ITypeSymbol? TryResolveNameofTypeFromConstructorArg(AttributeData attr, int argumentIndex, SemanticModel? semanticModel)
        {
            if(semanticModel is null)
                return null;

            var syntaxReference = attr.ApplicationSyntaxReference;
            if(syntaxReference?.GetSyntax() is not AttributeSyntax attributeSyntax)
                return null;

            var argumentList = attributeSyntax.ArgumentList;
            if(argumentList is null || argumentList.Arguments.Count <= argumentIndex)
                return null;

            var argument = argumentList.Arguments[argumentIndex];
            if(argument.NameEquals is not null)
                return null;

            return ResolveNameofExpressionType(argument.Expression, semanticModel);
        }

        /// <summary>
        /// Tries to resolve the type of a nameof() expression in a named argument.
        /// Returns null if the argument is not a nameof() expression or cannot be resolved.
        /// </summary>
        private static ITypeSymbol? TryResolveNameofTypeFromNamedArg(AttributeData attr, string argumentName, SemanticModel? semanticModel)
        {
            if(semanticModel is null)
                return null;

            var syntaxReference = attr.ApplicationSyntaxReference;
            if(syntaxReference?.GetSyntax() is not AttributeSyntax attributeSyntax)
                return null;

            var argumentList = attributeSyntax.ArgumentList;
            if(argumentList is null)
                return null;

            foreach(var argument in argumentList.Arguments)
            {
                if(argument.NameEquals?.Name.Identifier.Text == argumentName)
                {
                    return ResolveNameofExpressionType(argument.Expression, semanticModel);
                }
            }

            return null;
        }

        /// <summary>
        /// If the expression is a nameof() invocation, resolves the referenced symbol's type.
        /// Returns null for non-nameof expressions.
        /// </summary>
        private static ITypeSymbol? ResolveNameofExpressionType(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            if(expression is not InvocationExpressionSyntax invocation
                || invocation.Expression is not IdentifierNameSyntax identifierName
                || identifierName.Identifier.Text != "nameof"
                || invocation.ArgumentList.Arguments.Count != 1)
            {
                return null;
            }

            var nameofArgument = invocation.ArgumentList.Arguments[0].Expression;
            var symbolInfo = semanticModel.GetSymbolInfo(nameofArgument);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

            return symbol switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                IMethodSymbol method => method.ReturnType,
                ILocalSymbol local => local.Type,
                IParameterSymbol param => param.Type,
                _ => null,
            };
        }

        /// <summary>
        /// Extracts <see cref="GenericFactoryTypeMapping"/> from the registration attribute's
        /// <c>GenericFactoryTypeMapping</c> named property.
        /// Used as a fallback when <c>[IocGenericFactory]</c> is not present on the factory method.
        /// </summary>
        /// <returns>The generic factory type mapping, or null if not specified or invalid.</returns>
        public GenericFactoryTypeMapping? ExtractGenericFactoryMappingFromAttributeProperty()
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key != "GenericFactoryTypeMapping")
                    continue;

                if(namedArg.Value.Kind != TypedConstantKind.Array || namedArg.Value.IsNull)
                    return null;

                var typeArray = namedArg.Value.Values;
                if(typeArray.Length < 2)
                    return null;

                if(typeArray[0].Value is not INamedTypeSymbol serviceTypeTemplate)
                    return null;

                var serviceTypeTemplateData = serviceTypeTemplate.GetTypeData();

                var placeholderMap = new Dictionary<string, int>(StringComparer.Ordinal);
                for(int i = 1; i < typeArray.Length; i++)
                {
                    if(typeArray[i].Value is ITypeSymbol placeholderType)
                    {
                        var placeholderTypeName = placeholderType.FullyQualifiedName;
                        if(placeholderMap.ContainsKey(placeholderTypeName))
                            return null; // Duplicate placeholder
                        placeholderMap[placeholderTypeName] = i - 1;
                    }
                }

                if(placeholderMap.Count != typeArray.Length - 1)
                    return null;

                return new GenericFactoryTypeMapping(
                    serviceTypeTemplateData,
                    placeholderMap.ToImmutableEquatableDictionary());
            }

            return null;
        }

        /// <summary>
        /// Gets the Factory method data from the attribute, including parameter and return type information.
        /// When the resolved factory method is generic but has no <c>[IocGenericFactory]</c> attribute,
        /// falls back to the <c>GenericFactoryTypeMapping</c> property on the registration attribute.
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
                        var factoryData = CreateFactoryMethodData(methodSymbol);

                        // Fallback: if method is generic but has no [IocGenericFactory], check attribute's GenericFactoryTypeMapping
                        if(factoryData.GenericTypeMapping is null && methodSymbol.TypeParameters.Length > 0)
                        {
                            var mappingFromAttr = attribute.ExtractGenericFactoryMappingFromAttributeProperty();
                            if(mappingFromAttr is not null)
                                factoryData = factoryData with { GenericTypeMapping = mappingFromAttr };
                        }

                        return factoryData;
                    }

                    // Fallback: get path from nameof expression
                    var nameofPath = ResolveNameofExpression(nameofArgument, semanticModel)
                                     ?? nameofArgument.ToFullString().Trim();
                    return new FactoryMethodData(nameofPath, HasServiceProvider: true, HasKey: false, ReturnTypeName: null, AdditionalParameters: []);
                }

                // String literal - cannot determine parameters, assume full signature
                if(argument.Expression is LiteralExpressionSyntax literal &&
                   literal.Token.Value is string literalPath)
                {
                    return new FactoryMethodData(literalPath, HasServiceProvider: true, HasKey: false, ReturnTypeName: null, AdditionalParameters: []);
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
        /// <param name="semanticModel">Optional semantic model for resolving Factory method data.</param>
        /// <returns>The default settings model, or null if the attribute data is invalid.</returns>
        public DefaultSettingsModel? ExtractDefaultSettings(SemanticModel? semanticModel = null)
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

            // Get factory method data if semantic model is provided
            FactoryMethodData? factory = null;
            if(semanticModel is not null)
            {
                factory = attribute.GetFactoryMethodData(semanticModel);
            }

            // Get implementation types with constructor params and hierarchy (same as IocRegisterAttribute)
            var implementationTypes = attribute.GetImplementationTypes();

            return new DefaultSettingsModel(
                typeData,
                (ServiceLifetime)lifetime,
                registerAllInterfaces,
                registerAllBaseClasses,
                serviceTypes,
                decorators,
                tags,
                factory,
                implementationTypes);
        }

        /// <summary>
        /// Extracts default settings from a generic IoCRegisterDefaultsAttribute (e.g., IoCRegisterDefaultsAttribute&lt;T&gt;).
        /// The target service type is specified via type parameter instead of constructor argument.
        /// </summary>
        /// <param name="semanticModel">Optional semantic model for resolving Factory method data.</param>
        /// <returns>The default settings model, or null if the attribute data is invalid.</returns>
        public DefaultSettingsModel? ExtractDefaultSettingsFromGenericAttribute(SemanticModel? semanticModel = null)
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

            // Get factory method data if semantic model is provided
            FactoryMethodData? factory = null;
            if(semanticModel is not null)
            {
                factory = attribute.GetFactoryMethodData(semanticModel);
            }

            // Get implementation types with constructor params and hierarchy (same as IocRegisterAttribute)
            var implementationTypes = attribute.GetImplementationTypes();

            return new DefaultSettingsModel(
                typeData,
                (ServiceLifetime)lifetime,
                registerAllInterfaces,
                registerAllBaseClasses,
                serviceTypes,
                decorators,
                tags,
                factory,
                implementationTypes);
        }
    }

    extension(IMethodSymbol methodSymbol)
    {
        /// <summary>
        /// Creates FactoryMethodData from a method symbol.
        /// Analyzes factory method parameters:
        /// - IServiceProvider: Will be passed the service provider directly
        /// - [ServiceKey] attribute: Will be passed the registration key value
        /// - Other parameters: Will be resolved from the service provider using the same logic as [IocInject] methods
        /// Also extracts [IocGenericFactory] attribute if present for generic factory method support.
        /// </summary>
        public FactoryMethodData CreateFactoryMethodData()
        {
            var path = methodSymbol.FullAccessPath;
            bool hasServiceProvider = false;
            bool hasKey = false;
            List<ParameterData>? additionalParameters = null;

            foreach(var param in methodSymbol.Parameters)
            {
                var paramTypeName = param.Type.FullyQualifiedName;

                // Check for IServiceProvider
                if(paramTypeName is "global::System.IServiceProvider" or "System.IServiceProvider")
                {
                    hasServiceProvider = true;
                    continue;
                }

                // Check for [ServiceKey] attribute
                bool hasServiceKeyAttribute = false;
                foreach(var attribute in param.GetAttributes())
                {
                    var attrClass = attribute.AttributeClass;
                    if(attrClass is null)
                        continue;

                    if(attrClass.Name == "ServiceKeyAttribute"
                        && attrClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
                    {
                        hasServiceKeyAttribute = true;
                        hasKey = true;
                        break;
                    }
                }

                // Skip [ServiceKey] parameters from additional parameters
                if(hasServiceKeyAttribute)
                    continue;

                // Collect additional parameter info using the same logic as [IocInject] methods
                var (serviceKey, hasInjectAttribute, _, hasFromKeyedServicesAttribute) = param.GetServiceKeyAndAttributeInfo();
                var parameterData = new ParameterData(
                    param.Name,
                    param.Type.GetTypeData(),
                    IsNullable: param.NullableAnnotation == NullableAnnotation.Annotated,
                    HasDefaultValue: param.HasExplicitDefaultValue,
                    DefaultValue: param.HasExplicitDefaultValue ? ToDefaultValueCodeString(param.ExplicitDefaultValue) : null,
                    ServiceKey: serviceKey,
                    HasInjectAttribute: hasInjectAttribute,
                    HasServiceKeyAttribute: false, // Already handled above
                    HasFromKeyedServicesAttribute: hasFromKeyedServicesAttribute);

                additionalParameters ??= [];
                additionalParameters.Add(parameterData);
            }

            // Always store the return type for runtime comparison
            var returnTypeName = methodSymbol.ReturnType.FullyQualifiedName;

            // Extract [IocGenericFactory] attribute if present
            var genericTypeMapping = methodSymbol.ExtractGenericFactoryMapping();
            var typeParameterCount = methodSymbol.TypeParameters.Length;

            return new FactoryMethodData(
                path,
                hasServiceProvider,
                hasKey,
                returnTypeName,
                additionalParameters?.ToImmutableEquatableArray() ?? [],
                genericTypeMapping,
                typeParameterCount);
        }

        /// <summary>
        /// Extracts [IocGenericFactory] attribute from the method symbol and builds the type mapping.
        /// </summary>
        public GenericFactoryTypeMapping? ExtractGenericFactoryMapping()
        {
            // Only applicable to generic methods
            if(methodSymbol.TypeParameters.Length == 0)
            {
                return null;
            }

            // Find [IocGenericFactory] attribute
            AttributeData? genericFactoryAttr = null;
            foreach(var attr in methodSymbol.GetAttributes())
            {
                var attrClass = attr.AttributeClass;
                if(attrClass is null)
                    continue;

                var fullName = attrClass.ToDisplayString();
                if(fullName == Constants.IocGenericFactoryAttributeFullName)
                {
                    genericFactoryAttr = attr;
                    break;
                }
            }

            if(genericFactoryAttr is null)
            {
                return null;
            }

            // Extract GenericTypeMap array from constructor argument
            // [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
            // - First type: service type template with placeholders
            // - Following types: map to factory method type parameters in order
            if(genericFactoryAttr.ConstructorArguments.Length == 0)
            {
                return null;
            }

            var firstArg = genericFactoryAttr.ConstructorArguments[0];
            if(firstArg.Kind != TypedConstantKind.Array || firstArg.Values.IsDefaultOrEmpty)
            {
                return null;
            }

            var typeArray = firstArg.Values;
            if(typeArray.Length < 2)
            {
                return null; // Need at least service type template and one placeholder mapping
            }

            // First type is the service type template
            if(typeArray[0].Value is not INamedTypeSymbol serviceTypeTemplate)
            {
                return null;
            }

            var serviceTypeTemplateData = serviceTypeTemplate.GetTypeData();

            // Build placeholder to type parameter index map
            // Following types (index 1, 2, ...) map to factory method's type parameters (index 0, 1, ...)
            var placeholderMap = new Dictionary<string, int>(StringComparer.Ordinal);
            var expectedPlaceholderCount = typeArray.Length - 1;
            for(int i = 1; i < typeArray.Length; i++)
            {
                if(typeArray[i].Value is ITypeSymbol placeholderType)
                {
                    var placeholderTypeName = placeholderType.FullyQualifiedName;

                    // If the same placeholder type is used multiple times, the mapping is invalid
                    // because we cannot distinguish which type argument maps to which type parameter
                    if(placeholderMap.ContainsKey(placeholderTypeName))
                    {
                        return null;
                    }

                    // Map placeholder type to factory method's type parameter index (0-based)
                    placeholderMap[placeholderTypeName] = i - 1;
                }
            }

            // All placeholder types must be unique and present
            if(placeholderMap.Count != expectedPlaceholderCount)
            {
                return null;
            }

            return new GenericFactoryTypeMapping(
                serviceTypeTemplateData,
                placeholderMap.ToImmutableEquatableDictionary());
        }
    }

    extension(INamedTypeSymbol typeSymbol)
    {
        /// <summary>
        /// Extracts injection members (properties, fields, methods with [IocInject]/[Inject] attributes) from the type.
        /// This is used for both regular registrations and decorators.
        /// </summary>
        /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
        /// <returns>An array of injection member data.</returns>
        public ImmutableEquatableArray<InjectionMemberData> ExtractInjectionMembersForDecorator(SemanticModel? semanticModel = null)
        {
            List<InjectionMemberData>? injectionMembers = null;

            foreach(var (member, injectAttribute) in typeSymbol.GetInjectedMembers())
            {
                // Extract key information from IocInjectAttribute/InjectAttribute
                var (key, _, _) = injectAttribute.GetKeyInfo(semanticModel);

                InjectionMemberData? memberData = member switch
                {
                    IPropertySymbol property => CreateDecoratorPropertyInjection(property, key),
                    IFieldSymbol field => CreateDecoratorFieldInjection(field, key),
                    IMethodSymbol method => CreateDecoratorMethodInjection(method, key, semanticModel),
                    _ => null
                };

                if(memberData is not null)
                {
                    injectionMembers ??= [];
                    injectionMembers.Add(memberData);
                }
            }

            return injectionMembers?.ToImmutableEquatableArray() ?? [];
        }

        private static InjectionMemberData CreateDecoratorPropertyInjection(IPropertySymbol property, string? key)
        {
            var propertyType = property.Type.GetTypeData();
            var isNullable = property.NullableAnnotation == NullableAnnotation.Annotated;

            // Try to get the default value from property initializer
            var (hasDefaultValue, defaultValue) = GetDecoratorPropertyDefaultValue(property);

            return new InjectionMemberData(
                InjectionMemberType.Property,
                property.Name,
                propertyType,
                null,
                key,
                isNullable,
                hasDefaultValue,
                defaultValue);
        }

        private static InjectionMemberData CreateDecoratorFieldInjection(IFieldSymbol field, string? key)
        {
            var fieldType = field.Type.GetTypeData();
            var isNullable = field.NullableAnnotation == NullableAnnotation.Annotated;

            // Try to get the default value from field initializer
            var (hasDefaultValue, defaultValue) = GetDecoratorFieldDefaultValue(field);

            return new InjectionMemberData(
                InjectionMemberType.Field,
                field.Name,
                fieldType,
                null,
                key,
                isNullable,
                hasDefaultValue,
                defaultValue);
        }

        private static (bool HasDefaultValue, string? DefaultValue) GetDecoratorPropertyDefaultValue(IPropertySymbol property)
        {
            var syntaxRef = property.DeclaringSyntaxReferences.FirstOrDefault();
            if(syntaxRef?.GetSyntax() is not PropertyDeclarationSyntax propertySyntax)
                return (false, null);

            var initializer = propertySyntax.Initializer;
            if(initializer is null)
                return (false, null);

            // Check if it's a null literal or null-forgiving expression (null!)
            if(IsDecoratorNullExpression(initializer.Value))
            {
                return (true, null);
            }

            return (true, initializer.Value.ToString());
        }

        private static (bool HasDefaultValue, string? DefaultValue) GetDecoratorFieldDefaultValue(IFieldSymbol field)
        {
            var syntaxRef = field.DeclaringSyntaxReferences.FirstOrDefault();
            var syntax = syntaxRef?.GetSyntax();

            // Field can be declared in VariableDeclaratorSyntax
            EqualsValueClauseSyntax? initializer = syntax switch
            {
                VariableDeclaratorSyntax variableDeclarator => variableDeclarator.Initializer,
                _ => null
            };

            if(initializer is null)
                return (false, null);

            // Check if it's a null literal or null-forgiving expression (null!)
            if(IsDecoratorNullExpression(initializer.Value))
            {
                return (true, null);
            }

            return (true, initializer.Value.ToString());
        }

        private static bool IsDecoratorNullExpression(ExpressionSyntax expression)
        {
            // Direct null literal
            if(expression is LiteralExpressionSyntax literal &&
               literal.Kind() == SyntaxKind.NullLiteralExpression)
            {
                return true;
            }

            // Null-forgiving expression: null!
            if(expression is PostfixUnaryExpressionSyntax postfix &&
               postfix.Kind() == SyntaxKind.SuppressNullableWarningExpression &&
               postfix.Operand is LiteralExpressionSyntax innerLiteral &&
               innerLiteral.Kind() == SyntaxKind.NullLiteralExpression)
            {
                return true;
            }

            return false;
        }

        private static InjectionMemberData CreateDecoratorMethodInjection(IMethodSymbol method, string? key, SemanticModel? semanticModel)
        {
            var parameters = method.Parameters
                .Select(p =>
                {
                    var (serviceKey, hasInjectAttribute, hasServiceKeyAttribute, hasFromKeyedServicesAttribute) = p.GetServiceKeyAndAttributeInfo(semanticModel);
                    return new ParameterData(
                        p.Name,
                        p.Type.GetTypeData(),
                        IsNullable: p.NullableAnnotation == NullableAnnotation.Annotated,
                        HasDefaultValue: p.HasExplicitDefaultValue,
                        DefaultValue: p.HasExplicitDefaultValue ? DecoratorToDefaultValueCodeString(p.ExplicitDefaultValue) : null,
                        ServiceKey: serviceKey,
                        HasInjectAttribute: hasInjectAttribute,
                        HasServiceKeyAttribute: hasServiceKeyAttribute,
                        HasFromKeyedServicesAttribute: hasFromKeyedServicesAttribute);
                })
                .ToImmutableEquatableArray();

            return new InjectionMemberData(
                InjectionMemberType.Method,
                method.Name,
                null,
                parameters,
                key);
        }

        private static string? DecoratorToDefaultValueCodeString(object? value)
        {
            return value switch
            {
                null => null,
                string s => $"\"{s}\"",
                char c => $"'{c}'",
                bool b => b ? "true" : "false",
                _ => value.ToString()
            };
        }
    }
}
