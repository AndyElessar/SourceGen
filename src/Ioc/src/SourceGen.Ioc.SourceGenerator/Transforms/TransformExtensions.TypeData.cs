namespace SourceGen.Ioc;

internal static partial class TransformExtensions
{
    internal static string GetNameWithoutGeneric(string typeName)
    {
        int angleIndex = typeName.IndexOf('<');
        return angleIndex > 0 ? typeName[..angleIndex] : typeName;
    }

    private static WrapperKind NormalizeGeneratorWrapperKind(WrapperKind kind)
        => kind is WrapperKind.ValueTask ? WrapperKind.None : kind;

    /// <summary>
    /// Hierarchy data of type arguments declared in metadata, such as <c>string</c> in <c>Dictionary&lt;string, T&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Transforms rerun on every edit, but compilations with the same references share their metadata symbols, so this
    /// data is the same each time. Weak keys let the symbols and their compilations be collected.
    /// </remarks>
    private static readonly ConditionalWeakTable<INamedTypeSymbol, TypeArgHierarchy> MetadataTypeArgHierarchy = new();

    /// <summary>
    /// Supertypes of covariant type arguments declared in metadata, such as <c>string</c> in <c>IEnumerable&lt;string&gt;</c>,
    /// cached like <see cref="MetadataTypeArgHierarchy"/>.
    /// </summary>
    private static readonly ConditionalWeakTable<INamedTypeSymbol, TypeData> MetadataSupertypes = new();

    private sealed record class TypeArgHierarchy(
        TypeTraits Traits,
        ImmutableEquatableArray<TypeData>? AllInterfaces,
        ImmutableEquatableArray<TypeData>? AllBaseClasses);

    extension(ITypeSymbol typeSymbol)
    {
        public TypeData GetTypeData(
            bool extractConstructorParams = false,
            bool extractHierarchy = false)
        {
            if(typeSymbol is INamedTypeSymbol namedTypeSymbol)
                return namedTypeSymbol.GetTypeData(extractConstructorParams, extractHierarchy);

            // Handle array types specially - extract element type information
            if(typeSymbol is IArrayTypeSymbol arrayTypeSymbol)
                return arrayTypeSymbol.GetTypeData(extractConstructorParams, extractHierarchy);

            var name = typeSymbol.FullyQualifiedName;
            return TypeData.CreateSimple(name);
        }
    }

    extension(INamedTypeSymbol typeSymbol)
    {
        /// <summary>
        /// Gets the type data for this type symbol.
        /// </summary>
        /// <param name="extractConstructorParams">Whether to extract this type's constructor parameters.</param>
        /// <param name="extractHierarchy">Whether to extract all interfaces and base classes.</param>
        /// <param name="semanticModel">Optional semantic model for resolving nameof() expressions.</param>
        /// <param name="extractInjectionMembers">Whether to extract injection members (properties, fields, methods with [IocInject] attributes). Used for decorators.</param>
        public TypeData GetTypeData(
            bool extractConstructorParams = false,
            bool extractHierarchy = false,
            SemanticModel? semanticModel = null,
            bool extractInjectionMembers = false)
        {
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
            if(extractConstructorParams)
            {
                (constructorParams, hasInjectConstructor) = typeSymbol.ExtractConstructorParametersWithInfo(semanticModel);
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
            var isKnownNonDisposable = false;
            if(extractHierarchy)
            {
                // The hierarchy of an unbound generic type such as typeof(Decorator<,>) has no type arguments to match,
                // while its definition's hierarchy is written with its type parameters
                var hierarchySource = typeSymbol.IsUnboundGenericType ? typeSymbol.OriginalDefinition : typeSymbol;
                allInterfaces = hierarchySource.GetAllInterfaces();
                allBaseClasses = hierarchySource.GetAllBaseClasses();
                isKnownNonDisposable = hierarchySource.IsKnownNonDisposable();
            }

            return typeSymbol.CreateTypeData(
                typeName,
                typeParameters,
                constructorParams,
                hasInjectConstructor,
                injectionMembers,
                allInterfaces,
                allBaseClasses,
                isKnownNonDisposable);
        }

        /// <summary>
        /// Gets whether instances of exactly this type implement neither <c>IDisposable</c> nor <c>IAsyncDisposable</c>.
        /// </summary>
        /// <remarks>
        /// Returns <see langword="false"/> (unknown) when a source declaration of the type, one of its base classes, or one of
        /// its interfaces is partial, or when the hierarchy contains an error type: another source generator may add
        /// <c>IDisposable</c> to it, and this generator cannot see that generator's output.
        /// </remarks>
        public bool IsKnownNonDisposable()
        {
            if(typeSymbol.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                return false;
            }

            // The interfaces are checked before any declaration syntax is read, so disposable types need no syntax access
            var interfaces = typeSymbol.AllInterfaces;
            foreach(var iface in interfaces)
            {
                if(iface.SpecialType == SpecialType.System_IDisposable
                    || iface is { Name: "IAsyncDisposable", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } })
                {
                    return false;
                }
            }

            foreach(var iface in interfaces)
            {
                if(!IsHierarchyFinal(iface))
                {
                    return false;
                }
            }

            for(var type = typeSymbol; type is not null; type = type.BaseType)
            {
                if(!IsHierarchyFinal(type))
                {
                    return false;
                }
            }

            return true;

            // Metadata types are final; source types are final unless declared partial
            static bool IsHierarchyFinal(INamedTypeSymbol type)
            {
                if(type.TypeKind == TypeKind.Error)
                {
                    return false;
                }

                foreach(var reference in type.OriginalDefinition.DeclaringSyntaxReferences)
                {
                    if(reference.GetSyntax() is TypeDeclarationSyntax declaration
                        && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private TypeData CreateTypeData(
            string typeName,
            ImmutableEquatableArray<TypeParameter>? typeParameters,
            ImmutableEquatableArray<ParameterData>? constructorParams,
            bool hasInjectConstructor,
            ImmutableEquatableArray<InjectionMemberData>? injectionMembers,
            ImmutableEquatableArray<TypeData>? allInterfaces,
            ImmutableEquatableArray<TypeData>? allBaseClasses,
            bool isKnownNonDisposable)
        {
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
                    allBaseClasses,
                    IsKnownNonDisposable: isKnownNonDisposable);
            }

            return TypeData.CreateSimple(
                typeName,
                constructorParams,
                hasInjectConstructor,
                injectionMembers,
                allInterfaces,
                allBaseClasses,
                IsKnownNonDisposable: isKnownNonDisposable);
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
            using var parameters = new ValueListBuilder<TypeParameter>(typeParams.Length);

            for(int i = 0; i < typeParams.Length; i++)
            {
                var typeParam = typeParams[i];
                var typeArg = i < typeArgs.Length ? typeArgs[i] : null;

                // Create TypeData for the type argument
                var typeData = typeParam.CreateTypeDataForTypeArg(typeArg, depth);

                // Extract constraints only when requested (to avoid recursion in basic scenarios)
                ImmutableEquatableArray<TypeData>? constraintTypes = null;
                if(extractConstraints)
                {
                    constraintTypes = CreateConstraintTypes(typeParam.ConstraintTypes, depth);
                }

                parameters.Add(new TypeParameter(
                    typeParam.Name,
                    typeData,
                    constraintTypes,
                    typeParam.HasValueTypeConstraint,
                    typeParam.HasReferenceTypeConstraint,
                    typeParam.HasUnmanagedTypeConstraint,
                    typeParam.HasNotNullConstraint,
                    typeParam.HasConstructorConstraint,
                    typeParam.Variance == VarianceKind.Out));
            }

            return parameters.ToImmutableEquatableArray();

            static ImmutableEquatableArray<TypeData> CreateConstraintTypes(ImmutableArray<ITypeSymbol> constraints, int depth)
            {
                // Most type parameters have no constraint types
                if(constraints.IsEmpty)
                {
                    return [];
                }

                using var result = new ValueListBuilder<TypeData>(constraints.Length);
                foreach(var constraint in constraints)
                {
                    result.Add(constraint is INamedTypeSymbol namedConstraint
                        ? namedConstraint.CreateBasicTypeData(depth + 1)
                        : CreateConstraintTypeData(constraint));
                }
                return result.ToImmutableEquatableArray();
            }

            // Creates TypeData for a constraint that is not a named type (a type parameter or an array, for example).
            static TypeData CreateConstraintTypeData(ITypeSymbol constraint)
            {
                var name = constraint.FullyQualifiedName;
                return constraint.TypeKind == TypeKind.TypeParameter
                    ? TypeData.CreateTypeParameter(name)
                    : TypeData.CreateGeneric(name, GetNameWithoutGeneric(name), constraint.ContainsGenericParameters, 0, false);
            }
        }

        /// <summary>
        /// Gets all interfaces implemented by a type.
        /// Creates basic TypeData without recursive type parameter extraction to avoid circular dependencies.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetAllInterfaces()
        {
            var interfaces = typeSymbol.AllInterfaces;
            using var result = new ValueListBuilder<TypeData>(interfaces.Length);
            foreach(var iface in interfaces)
            {
                result.Add(iface.CreateBasicTypeData());
            }

            return result.ToImmutableEquatableArray();
        }

        /// <summary>
        /// Gets all base classes of a type, excluding System.Object.
        /// Creates basic TypeData without recursive type parameter extraction to avoid circular dependencies.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetAllBaseClasses()
        {
            using var result = new ValueListBuilder<TypeData>(4);
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
        /// <param name="depth">Current recursion depth to prevent infinite recursion.</param>
        /// <param name="traits">Traits of a type argument, used to check special constraints.</param>
        /// <param name="allInterfaces">Interfaces of a type argument, used to check type constraints.</param>
        /// <param name="allBaseClasses">Base classes of a type argument, used to check type constraints.</param>
        public TypeData CreateBasicTypeData(
            int depth = 0,
            TypeTraits traits = default,
            ImmutableEquatableArray<TypeData>? allInterfaces = null,
            ImmutableEquatableArray<TypeData>? allBaseClasses = null)
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
                    typeParameters,
                    AllInterfaces: allInterfaces,
                    AllBaseClasses: allBaseClasses,
                    Traits: traits);
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
                    AllInterfaces: allInterfaces,
                    AllBaseClasses: allBaseClasses,
                    Traits: traits);
            }

            return TypeData.CreateSimple(typeName, AllInterfaces: allInterfaces, AllBaseClasses: allBaseClasses, Traits: traits);
        }
    }

    extension(ITypeParameterSymbol typeParam)
    {
        /// <summary>
        /// Creates TypeData for a type argument, with the traits, interfaces and base classes that constraint checks use.
        /// </summary>
        public TypeData CreateTypeDataForTypeArg(
            ITypeSymbol? typeArg,
            int depth)
        {
            if(typeArg is INamedTypeSymbol namedArg && typeArg.TypeKind != TypeKind.TypeParameter)
            {
                // For concrete types, recursively extract type parameters and interfaces
                // Annotated type arguments are new symbol objects on every access, so key the cache by the unannotated symbol.
                var hierarchy = namedArg.DeclaringSyntaxReferences.IsEmpty && namedArg.TypeKind != TypeKind.Error
                    ? MetadataTypeArgHierarchy.GetValue((INamedTypeSymbol)namedArg.WithNullableAnnotation(NullableAnnotation.None), CreateHierarchy)
                    : CreateHierarchy(namedArg);

                return namedArg.CreateBasicTypeData(depth + 1, hierarchy.Traits, hierarchy.AllInterfaces, hierarchy.AllBaseClasses);
            }

            // Arrays keep their element type, created like any type argument, so T[] and T[][] can be matched against
            // closed arrays such as Req[] and Req[][], and the element keeps its interfaces for constraint checks.
            // The array itself keeps its interfaces and System.Array, so where T : IEnumerable<int> accepts int[].
            if(typeArg is IArrayTypeSymbol arrayArg)
            {
                var elementType = typeParam.CreateTypeDataForTypeArg(arrayArg.ElementType, depth + 1);
                var arrayName = arrayArg.FullyQualifiedName;
                var arrayHierarchy = CreateHierarchy(arrayArg);
                return TypeData.CreateWrapper(
                    arrayName,
                    arrayName,
                    arrayArg.ContainsGenericParameters,
                    GenericArity: 1,
                    WrapperKind.Array,
                    IsNestedOpenGeneric: false,
                    TypeParameters: [new TypeParameter("T", elementType)],
                    AllInterfaces: arrayHierarchy.AllInterfaces,
                    AllBaseClasses: arrayHierarchy.AllBaseClasses,
                    Traits: arrayHierarchy.Traits);
            }

            if(typeArg is not null)
            {
                var argName = typeArg.FullyQualifiedName;
                return typeArg.TypeKind == TypeKind.TypeParameter
                    ? TypeData.CreateTypeParameter(argName)
                    : TypeData.CreateGeneric(
                        argName,
                        GetNameWithoutGeneric(argName),
                        typeArg.ContainsGenericParameters,
                        0,
                        Traits: GetTypeTraits(typeArg));
            }

            // No type argument available, this is a type parameter placeholder
            return TypeData.CreateTypeParameter(typeParam.Name);

            static TypeArgHierarchy CreateHierarchy(ITypeSymbol type)
            {
                // Base classes are needed to check base class constraints; names are enough, and avoid recursing
                // through self-referencing bases such as Foo : Base<Foo>. Structs, enums and delegates have base classes
                // too, such as System.Enum for where T : Enum. System.Object is satisfied by every type, so it is omitted.
                ImmutableEquatableArray<TypeData>? baseClasses = null;
                if(type.BaseType is { SpecialType: not SpecialType.System_Object })
                {
                    using var bases = new ValueListBuilder<TypeData>(4);
                    for(var baseType = type.BaseType; baseType is { SpecialType: not SpecialType.System_Object }; baseType = baseType.BaseType)
                    {
                        bases.Add(CreateInterfaceTypeData(baseType));
                    }
                    baseClasses = bases.ToImmutableEquatableArray();
                }

                return new(GetTypeTraits(type), CreateConvertibleInterfaceTypeData(type), baseClasses);
            }

            // Records the traits that special constraints check, such as where T : struct
            static TypeTraits GetTypeTraits(ITypeSymbol type)
            {
                var traits = TypeTraits.Known;
                if(type.IsReferenceType)
                {
                    traits |= TypeTraits.ReferenceType;
                }
                if(type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
                {
                    traits |= TypeTraits.NonNullableValueType;
                }
                if(type.IsUnmanagedType)
                {
                    traits |= TypeTraits.Unmanaged;
                }
                if(type.SatisfiesConstructorConstraint())
                {
                    traits |= TypeTraits.DefaultConstructor;
                }

                return traits;
            }

            // Creates the interfaces of a type argument. Covariant ones, such as IEnumerable<string> of List<string>, keep
            // their type arguments and variance, so constraint checks convert them through covariance (to
            // IEnumerable<object>), also after their type parameters are substituted, such as IEnumerable<T> of List<T>.
            static ImmutableEquatableArray<TypeData>? CreateConvertibleInterfaceTypeData(ITypeSymbol type)
            {
                var interfaces = type.AllInterfaces;
                if(interfaces.Length == 0)
                {
                    return null;
                }

                using var result = new ValueListBuilder<TypeData>(interfaces.Length);
                foreach(var iface in interfaces)
                {
                    result.Add(iface.IsCovariantGeneric && iface.ContainingType is not { IsGenericType: true }
                        ? CreateCovariantInterfaceTypeData(iface)
                        : CreateInterfaceTypeData(iface));
                }

                return result.ToImmutableEquatableArray();
            }

            // Creates a covariant interface with its type arguments: the supertypes of a covariant one (see
            // CreateSupertypeData), and the name of an invariant or contravariant one, which only matches itself.
            // An interface nested in a generic type has type arguments its own do not show, so it is matched by name only.
            static TypeData CreateCovariantInterfaceTypeData(INamedTypeSymbol iface)
            {
                var definition = iface.OriginalDefinition;
                using var typeParameters = new ValueListBuilder<TypeParameter>(iface.TypeArguments.Length);
                for(var i = 0; i < iface.TypeArguments.Length; i++)
                {
                    var argument = iface.TypeArguments[i];
                    var isCovariant = definition.TypeParameters[i].Variance == VarianceKind.Out;
                    var argumentData = argument.TypeKind == TypeKind.TypeParameter
                        ? TypeData.CreateTypeParameter(argument.FullyQualifiedName)
                        : isCovariant ? CreateSupertypeData(argument) : TypeData.CreateSimple(argument.FullyQualifiedName);
                    typeParameters.Add(new TypeParameter(definition.TypeParameters[i].Name, argumentData, IsCovariant: isCovariant));
                }

                var name = iface.FullyQualifiedName;
                return TypeData.CreateGeneric(
                    name,
                    GetNameWithoutGeneric(name),
                    iface.ContainsGenericParameters,
                    iface.Arity,
                    false,
                    typeParameters.ToImmutableEquatableArray());
            }

            // Creates the supertypes a covariant type argument converts to, one level deep: its traits (only a reference
            // type converts), and the names of its interfaces and base classes.
            static TypeData CreateSupertypeData(ITypeSymbol type)
            {
                if(!type.IsReferenceType)
                {
                    return TypeData.CreateSimple(type.FullyQualifiedName, Traits: GetTypeTraits(type));
                }

                if(type is INamedTypeSymbol namedType && namedType.DeclaringSyntaxReferences.IsEmpty && namedType.TypeKind != TypeKind.Error)
                {
                    return MetadataSupertypes.GetValue((INamedTypeSymbol)namedType.WithNullableAnnotation(NullableAnnotation.None), CreateSupertypes);
                }

                return CreateSupertypes(type);

                static TypeData CreateSupertypes(ITypeSymbol type)
                {
                    ImmutableEquatableArray<TypeData>? interfaces = null;
                    if(type.AllInterfaces.Length > 0)
                    {
                        using var result = new ValueListBuilder<TypeData>(type.AllInterfaces.Length);
                        foreach(var iface in type.AllInterfaces)
                        {
                            result.Add(CreateInterfaceTypeData(iface));
                        }
                        interfaces = result.ToImmutableEquatableArray();
                    }

                    ImmutableEquatableArray<TypeData>? baseClasses = null;
                    if(type.BaseType is { SpecialType: not SpecialType.System_Object })
                    {
                        using var bases = new ValueListBuilder<TypeData>(4);
                        for(var baseType = type.BaseType; baseType is { SpecialType: not SpecialType.System_Object }; baseType = baseType.BaseType)
                        {
                            bases.Add(CreateInterfaceTypeData(baseType));
                        }
                        baseClasses = bases.ToImmutableEquatableArray();
                    }

                    return TypeData.CreateSimple(type.FullyQualifiedName, AllInterfaces: interfaces, AllBaseClasses: baseClasses, Traits: GetTypeTraits(type));
                }
            }

            // Creates a simple TypeData for an interface or base class type.
            static TypeData CreateInterfaceTypeData(INamedTypeSymbol iface)
            {
                var name = iface.FullyQualifiedName;
                return iface.IsGenericType || iface.Arity > 0
                    ? TypeData.CreateGeneric(
                        name,
                        GetNameWithoutGeneric(name),
                        iface.ContainsGenericParameters,
                        iface.Arity,
                        false)
                    : TypeData.CreateSimple(name);
            }
        }
    }

    extension(IArrayTypeSymbol arrayTypeSymbol)
    {
        public TypeData GetTypeData(
            bool extractConstructorParams = false,
            bool extractHierarchy = false)
        {
            var elementType = arrayTypeSymbol.ElementType;
            var typeName = arrayTypeSymbol.FullyQualifiedName;

            // For arrays, create TypeData with element type as a pseudo-TypeParameter
            // This allows TryGetArrayElementType to extract the element type
            ImmutableEquatableArray<TypeParameter> typeParameters;
            if(elementType is INamedTypeSymbol namedElementType)
            {
                var elementTypeData = namedElementType.GetTypeData(extractConstructorParams, extractHierarchy);
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
