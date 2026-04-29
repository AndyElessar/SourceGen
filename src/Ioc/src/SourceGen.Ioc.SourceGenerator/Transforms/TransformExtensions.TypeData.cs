namespace SourceGen.Ioc.SourceGenerator.Models;

internal static partial class TransformExtensions
{
    private static string GetNameWithoutGeneric(string typeName)
    {
        int angleIndex = typeName.IndexOf('<');
        return angleIndex > 0 ? typeName[..angleIndex] : typeName;
    }

    private static WrapperKind NormalizeGeneratorWrapperKind(WrapperKind kind)
        => kind is WrapperKind.ValueTask ? WrapperKind.None : kind;

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
            var wrapperKind = typeSymbol.TryGetWrapperInfo(out var wrapperInfo)
                ? NormalizeGeneratorWrapperKind(wrapperInfo.Kind)
                : WrapperKind.None;

            if(wrapperKind is not WrapperKind.None && typeSymbol.TypeArguments.Length > 0)
            {
                foreach(var typeArgument in typeSymbol.TypeArguments.OfType<INamedTypeSymbol>())
                {
                    if(!typeArgument.TryGetWrapperInfo(out var childWrapperInfo))
                    {
                        continue;
                    }

                    var childWrapperKind = NormalizeGeneratorWrapperKind(childWrapperInfo.Kind);
                    if(childWrapperKind is WrapperKind.None)
                    {
                        continue;
                    }

                    // Keep generator behavior for collection wrappers: do not downgrade
                    // IEnumerable<Lazy<T>> / IEnumerable<Func<T>> at this classification stage.
                    // Collection nesting limitations are enforced by wrapper resolver generation.
                    if(IsUnsupportedWrapperNesting(wrapperKind, childWrapperKind, isAfterCollection: false))
                    {
                        wrapperKind = WrapperKind.None;
                        break;
                    }
                }
            }

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
            var wrapperKind = typeSymbol.TryGetWrapperInfo(out var wrapperInfo)
                ? NormalizeGeneratorWrapperKind(wrapperInfo.Kind)
                : WrapperKind.None;

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
}