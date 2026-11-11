namespace SourceGen.Ioc;

partial class ContainerAnalyzer
{
    /// <summary>
    /// Gets whether the generator can close an open generic implementation for a closed service type: like its
    /// <c>TryGenerateClosedGenericRegistration</c>, the service type as the implementation declares it must match the
    /// closed service type, determine every type parameter, and the type arguments must satisfy the constraints.
    /// </summary>
    /// <param name="implementation">The generic type definition of the implementation.</param>
    private static bool CanCloseOpenGeneric(INamedTypeSymbol implementation, INamedTypeSymbol serviceType)
    {
        // An open generic Factory registration exposes the service type itself, which its factory creates
        if(implementation.TypeKind is TypeKind.Interface || implementation.IsAbstract)
            return true;

        // Without a declared form, the generator maps the service type by its definition's type parameters
        if(FindDeclaredForm(implementation, serviceType.OriginalDefinition) is not { } declaredForm)
            return true;

        var typeArguments = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        if(!TryMatchTypeArguments(declaredForm, serviceType, typeArguments))
            return false;

        foreach(var typeParameter in implementation.TypeParameters)
        {
            if(!typeArguments.TryGetValue(typeParameter, out var typeArgument)
                || !SatisfiesTypeParameterConstraints(typeParameter, typeArgument, typeArguments))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Finds the form of a generic type definition that an implementation declares: itself, one of its base classes, or
    /// one of its interfaces, written with the implementation's type parameters.
    /// </summary>
    private static INamedTypeSymbol? FindDeclaredForm(INamedTypeSymbol implementation, INamedTypeSymbol definition)
    {
        for(var type = implementation; type is not null; type = type.BaseType)
        {
            if(SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, definition))
                return type;
        }

        foreach(var candidate in implementation.AllInterfaces)
        {
            if(SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, definition))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Matches a type written with type parameters against a closed type, recording what each type parameter stands for.
    /// </summary>
    private static bool TryMatchTypeArguments(ITypeSymbol pattern, ITypeSymbol closed, Dictionary<ITypeParameterSymbol, ITypeSymbol> typeArguments)
    {
        switch(pattern)
        {
            case ITypeParameterSymbol typeParameter:
                if(typeArguments.TryGetValue(typeParameter, out var mapped))
                    return SymbolEqualityComparer.Default.Equals(mapped, closed);

                typeArguments.Add(typeParameter, closed);
                return true;

            case IArrayTypeSymbol patternArray:
                return closed is IArrayTypeSymbol closedArray
                    && patternArray.Rank == closedArray.Rank
                    && TryMatchTypeArguments(patternArray.ElementType, closedArray.ElementType, typeArguments);

            case INamedTypeSymbol { IsGenericType: true } patternType:
                if(closed is not INamedTypeSymbol closedType
                    || !SymbolEqualityComparer.Default.Equals(patternType.OriginalDefinition, closedType.OriginalDefinition))
                {
                    return false;
                }

                for(var i = 0; i < patternType.TypeArguments.Length; i++)
                {
                    if(!TryMatchTypeArguments(patternType.TypeArguments[i], closedType.TypeArguments[i], typeArguments))
                        return false;
                }

                return true;

            default:
                return SymbolEqualityComparer.Default.Equals(pattern, closed);
        }
    }

    /// <summary>
    /// Checks the <c>class</c>, <c>struct</c>, <c>unmanaged</c>, <c>new()</c> and type constraints of a type parameter.
    /// <c>notnull</c> only produces nullable warnings, so it is not checked.
    /// </summary>
    private static bool SatisfiesTypeParameterConstraints(
        ITypeParameterSymbol typeParameter,
        ITypeSymbol typeArgument,
        Dictionary<ITypeParameterSymbol, ITypeSymbol> typeArguments)
    {
        var isNonNullableValueType = typeArgument.IsValueType
            && typeArgument.OriginalDefinition.SpecialType is not SpecialType.System_Nullable_T;

        if((typeParameter.HasReferenceTypeConstraint && !typeArgument.IsReferenceType)
            || (typeParameter.HasValueTypeConstraint && !isNonNullableValueType)
            || (typeParameter.HasUnmanagedTypeConstraint && !(isNonNullableValueType && typeArgument.IsUnmanagedType))
            || (typeParameter.HasConstructorConstraint && !HasPublicParameterlessConstructor(typeArgument)))
        {
            return false;
        }

        foreach(var constraintType in typeParameter.ConstraintTypes)
        {
            // A constraint that cannot be closed here (e.g. an array of a type parameter) is accepted
            if(SubstituteConstraint(constraintType, typeArguments) is { } closedConstraint
                && !IsImplicitlyConvertible(typeArgument, closedConstraint))
            {
                return false;
            }
        }

        return true;

        static bool HasPublicParameterlessConstructor(ITypeSymbol type)
            => type.IsValueType
                || type is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } namedType
                    && namedType.InstanceConstructors.Any(static c => c.Parameters.IsEmpty && c.DeclaredAccessibility is Accessibility.Public);
    }

    /// <summary>
    /// Closes a constraint type with the matched type arguments, such as <c>IRequest&lt;string&gt;</c> for
    /// <c>where TRequest : IRequest&lt;TResponse&gt;</c> with <c>TResponse = string</c>.
    /// </summary>
    /// <returns><see langword="null"/> when the constraint cannot be closed.</returns>
    private static ITypeSymbol? SubstituteConstraint(ITypeSymbol type, Dictionary<ITypeParameterSymbol, ITypeSymbol> typeArguments)
    {
        switch(type)
        {
            case ITypeParameterSymbol typeParameter:
                return typeArguments.TryGetValue(typeParameter, out var typeArgument) ? typeArgument : null;

            case INamedTypeSymbol { IsGenericType: true } namedType:
                if(namedType.ContainingType is { IsGenericType: true })
                    return null;

                var closedArguments = new ITypeSymbol[namedType.TypeArguments.Length];
                for(var i = 0; i < closedArguments.Length; i++)
                {
                    if(SubstituteConstraint(namedType.TypeArguments[i], typeArguments) is not { } closedArgument)
                        return null;

                    closedArguments[i] = closedArgument;
                }

                return namedType.OriginalDefinition.Construct(closedArguments);

            default:
                return ContainsTypeParameter(type) ? null : type;
        }
    }

    /// <summary>
    /// Gets whether a type argument converts to a constraint type by an identity, implicit reference, or boxing
    /// conversion, including variant interface and delegate conversions.
    /// </summary>
    private static bool IsImplicitlyConvertible(ITypeSymbol source, ITypeSymbol target)
    {
        if(SymbolEqualityComparer.Default.Equals(source, target) || target.SpecialType is SpecialType.System_Object)
            return true;

        for(var baseType = source.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if(IsVariantConvertible(baseType, target))
                return true;
        }

        if(source is INamedTypeSymbol namedSource && IsVariantConvertible(namedSource, target))
            return true;

        foreach(var implemented in source.AllInterfaces)
        {
            if(IsVariantConvertible(implemented, target))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets whether a type is the target type or converts to it through the variance of its type parameters, such as
    /// <c>IEnumerable&lt;string&gt;</c> to <c>IEnumerable&lt;object&gt;</c>.
    /// </summary>
    private static bool IsVariantConvertible(INamedTypeSymbol source, ITypeSymbol target)
    {
        if(SymbolEqualityComparer.Default.Equals(source, target))
            return true;

        if(target is not INamedTypeSymbol { IsGenericType: true } namedTarget
            || source.TypeKind is not (TypeKind.Interface or TypeKind.Delegate)
            || !SymbolEqualityComparer.Default.Equals(source.OriginalDefinition, namedTarget.OriginalDefinition))
        {
            return false;
        }

        var typeParameters = source.OriginalDefinition.TypeParameters;
        for(var i = 0; i < typeParameters.Length; i++)
        {
            var sourceArgument = source.TypeArguments[i];
            var targetArgument = namedTarget.TypeArguments[i];
            if(SymbolEqualityComparer.Default.Equals(sourceArgument, targetArgument))
                continue;

            // Variant conversions require reference conversions of the type arguments
            var convertible = typeParameters[i].Variance switch
            {
                VarianceKind.Out => sourceArgument.IsReferenceType && IsImplicitlyConvertible(sourceArgument, targetArgument),
                VarianceKind.In => targetArgument.IsReferenceType && IsImplicitlyConvertible(targetArgument, sourceArgument),
                _ => false,
            };
            if(!convertible)
                return false;
        }

        return true;
    }
}
